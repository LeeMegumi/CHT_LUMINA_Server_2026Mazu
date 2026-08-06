using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

/// <summary>
/// KIOSK 連線 API 管理器（v4：改為手動保管 Cookie 原字串）
///
/// 端點（由 avatar.cht.com.tw/komi/kiosk 的 DevTools 確認）：
///   Step 1: POST /auth/fixed                    → Set-Cookie: avatarBackendToken=&lt;JWT&gt;
///   Step 2: POST /streaming/start
///   Step 3: POST /streaming/getTurnInformation
///   Step 4: POST /streaming/createOffer （由 KioskWebRTCManager 呼叫）
///
/// ⚠️ 為什麼不用 CookieContainer / UnityWebRequest 的 Cookie 快取：
///   兩者都會對 Cookie 值做正規化與網域比對，實測後端回 JwtSignatureException
///   （代表送到的 JWT 與簽發的不一致，或根本沒送到）。
///   本版改為：從 Set-Cookie 用 Regex 取出 JWT 原字串，之後每次請求
///   直接以 raw header「Cookie: avatarBackendToken=xxx」送出，不經任何中間層。
///   並在取得與送出兩個時點都印出長度與頭尾片段，可直接跟 DevTools 對照。
/// </summary>
public class KioskAPIManager : MonoBehaviour
{
    public enum HttpMode
    {
        HttpClient,        // System.Net.Http.HttpClient + UseCookies=false（推薦）
        SystemHttp,        // .NET HttpWebRequest
        UnityWebRequest    // Unity 內建（WebGL 只能用這個）
    }

    [Header("連線方式")]
    [Tooltip("HttpClient：UseCookies=false，Set-Cookie 會完整留在回應標頭（推薦）\n" +
             "SystemHttp：.NET HttpWebRequest\n" +
             "UnityWebRequest：Unity 內建，WebGL 平台必須用這個")]
    [SerializeField] public HttpMode httpMode = HttpMode.HttpClient;

    [Header("後端設定")]
    [SerializeField] public string BASE_URL = "https://backend.avatar.cht.com.tw";
    [SerializeField] public string AUTH_ENDPOINT = "/auth/fixed";
    [SerializeField] public string STREAMING_START_ENDPOINT = "/streaming/start";
    [SerializeField] public string TURN_ENDPOINT = "/streaming/getTurnInformation";
    [SerializeField] public string OFFER_ENDPOINT = "/streaming/createOffer";

    [Header("Kiosk 登入資訊")]
    // ✅ 已由 JWT payload 確認：內含 "companyId":"komi"
    [SerializeField] public string companyId = "komi";
    [SerializeField] public string quickLoginCode = "V8QDFG1LDN";

    [Header("Cookie / 除錯")]
    [SerializeField] public string cookieName = "avatarBackendToken";

    [Tooltip("印出每次請求送出的 Cookie 長度與頭尾片段，可直接跟 DevTools 對照")]
    [SerializeField] public bool verboseLog = true;

    // ⚠️ 測試用捷徑：填了就跳過 Step 1，直接拿這組 Token 測後面的流程。
    //
    // 注意：Token 的 payload 內含 streamingUseCode（該次配發的串流編號），
    // 是「一次性」的。從瀏覽器複製來的 Token，一旦瀏覽器那邊的 session 結束，
    // 對應的串流資料就被釋放，再拿來用會得到 StreamingNotFoundException。
    // 正常情況請留空，讓 Unity 自己跑 /auth/fixed 取得新的 Token。
    //
    // ⚠️ 若為了除錯貼上 Token，測完請清空，不要 commit 進 git。
    [Tooltip("除錯用：貼上 DevTools 抓到的 avatarBackendToken 值（只要 JWT 本身，不含名稱）。\n" +
             "填了就跳過 Step 1。正常使用請留空！")]
    [TextArea(2, 6)]
    [SerializeField] public string debugTokenOverride = "";

    // ===== 連線狀態 =====
    [HideInInspector] public bool isAuthSuccess;
    [HideInInspector] public bool isStreamingStarted;
    [HideInInspector] public bool isTurnReady;
    [HideInInspector] public string lastErrorCode;
    [HideInInspector] public string lastErrorMessage;

    // ===== 取得的資料 =====
    public string accessToken;
    public TurnInformation turnInfo;
    public StreamingSettings streamingSettings;

    public double cookieExpireUnixTime;

    /// <summary>從 Set-Cookie 取出的 JWT 原字串（不含 cookie 名稱）</summary>
    private string authToken;

    private const int REQUEST_TIMEOUT_SEC = 40;

    #region 資料結構

    [System.Serializable]
    public class TurnInformation
    {
        public string username;
        public string credential;
        public string[] urls;
    }

    [System.Serializable]
    public class StreamingSettings
    {
        public string backgroundtype;
        public string backgroundColorCode;
        public string avatarLogoFileUrl;
        public string avatarDefaultLanguage;
        public string[] avatarLanguages;
    }

    public class HttpResponse
    {
        public int status;
        public string text;
        public string setCookie;        // raw Set-Cookie 標頭（可能為 null）
        public string capturedToken;    // 由 CookieContainer 直接取出的 Token（較可靠）
        public string allHeaders;       // 除錯用：完整回應標頭
        public string networkError;
        public bool IsSuccess => networkError == null && status >= 200 && status < 300;
    }

    #endregion

    #region 共用：送出請求

    /// <summary>目前要送出的 Cookie header 值，例 "avatarBackendToken=eyJhbG..."</summary>
    private string CookieHeaderValue =>
        string.IsNullOrEmpty(authToken) ? null : cookieName + "=" + authToken;

    /// <summary>
    /// 送出 JSON POST（自動依 httpMode 選路線、自動帶 Cookie）。
    /// KioskWebRTCManager 送 createOffer 也用這個。
    /// </summary>
    public IEnumerator PostJson(string endpoint, string jsonBody, HttpResponse result)
    {
        if (verboseLog) LogOutgoingCookie(endpoint);

        switch (httpMode)
        {
            case HttpMode.HttpClient:
                yield return HttpClientPost(endpoint, jsonBody, result);
                break;
            case HttpMode.SystemHttp:
                // 驗證時不掛 CookieContainer，否則 Mono 會把 Set-Cookie 從標頭吃掉
                yield return SystemHttpPost(endpoint, jsonBody, result, false);
                break;
            default:
                yield return UnityHttpPost(endpoint, jsonBody, result);
                break;
        }

        if (verboseLog)
            Debug.Log($"🌐 POST {endpoint} → {result.status}");
    }

    private static System.Net.Http.HttpClient _client;

    /// <summary>
    /// 共用的 HttpClient。關鍵設定 UseCookies = false：
    /// 讓 handler 不要自作主張處理 Cookie，Set-Cookie 就會原封不動留在回應標頭裡。
    /// 這正是 HttpWebRequest / UnityWebRequest 在 Mono 上讀不到 Set-Cookie 的原因——
    /// 它們都會把 Cookie 收進內部容器，而該容器又因網域比對規則把 Cookie 丟掉。
    /// </summary>
    private static System.Net.Http.HttpClient Client
    {
        get
        {
            if (_client == null)
            {
                var handler = new System.Net.Http.HttpClientHandler();
                try { handler.UseCookies = false; } catch { }
                _client = new System.Net.Http.HttpClient(handler);
                _client.Timeout = System.TimeSpan.FromSeconds(REQUEST_TIMEOUT_SEC);
            }
            return _client;
        }
    }

    /// <summary>路線 A（預設）：HttpClient + UseCookies=false，Set-Cookie 完整保留</summary>
    private IEnumerator HttpClientPost(string endpoint, string jsonBody, HttpResponse result)
    {
        string url = BASE_URL + endpoint;
        string cookieHeader = CookieHeaderValue;

        var task = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                using (var req = new System.Net.Http.HttpRequestMessage(
                           System.Net.Http.HttpMethod.Post, url))
                {
                    req.Content = new System.Net.Http.StringContent(
                        jsonBody, Encoding.UTF8, "application/json");
                    req.Headers.TryAddWithoutValidation("Accept", "application/json");

                    if (!string.IsNullOrEmpty(cookieHeader))
                        req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

                    using (var resp = await Client.SendAsync(req))
                    {
                        result.status = (int)resp.StatusCode;
                        result.text = await resp.Content.ReadAsStringAsync();

                        var sb = new StringBuilder();

                        foreach (var h in resp.Headers)
                        {
                            string joined = string.Join(" | ", h.Value);
                            sb.AppendLine($"  {h.Key}: {joined}");
                            if (h.Key.ToLower().Contains("cookie"))
                            {
                                result.setCookie = string.IsNullOrEmpty(result.setCookie)
                                    ? joined : result.setCookie + "\n" + joined;
                            }
                        }
                        foreach (var h in resp.Content.Headers)
                            sb.AppendLine($"  {h.Key}: {string.Join(" | ", h.Value)}");

                        result.allHeaders = sb.ToString();
                    }
                }
            }
            catch (System.Exception e)
            {
                result.networkError = e.Message;
            }
        });

        while (!task.IsCompleted) yield return null;
    }

    /// <summary>
    /// 路線 A：.NET HttpWebRequest（背景執行緒，不卡畫面）
    ///
    /// captureCookies = true（驗證時）：
    ///   掛上 CookieContainer，讓 .NET 幫忙解析 Set-Cookie，
    ///   再從 resp.Cookies 取出 Token。這比讀 raw 標頭可靠——
    ///   Mono 在掛了 Container 時會把 Set-Cookie 從 Headers 中移除，
    ///   反之某些版本又完全不暴露該標頭，所以三種來源都試一次。
    ///
    /// captureCookies = false（後續請求）：
    ///   不掛 Container（避免 Domain=avatar.cht.com.tw 對 backend.avatar.cht.com.tw
    ///   的比對在 Mono 上失敗導致 Cookie 沒送出），改用 raw header 原字串送出。
    /// </summary>
    private IEnumerator SystemHttpPost(string endpoint, string jsonBody, HttpResponse result, bool captureCookies)
    {
        string url = BASE_URL + endpoint;
        string cookieHeader = CookieHeaderValue;
        string name = cookieName;
        bool done = false;

        System.Threading.Tasks.Task.Run(() =>
        {
            System.Net.CookieContainer container = null;
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Accept = "application/json";
                req.Timeout = REQUEST_TIMEOUT_SEC * 1000;
                req.ReadWriteTimeout = REQUEST_TIMEOUT_SEC * 1000;

                if (captureCookies)
                {
                    container = new System.Net.CookieContainer();
                    req.CookieContainer = container;
                }
                else if (!string.IsNullOrEmpty(cookieHeader))
                {
                    req.Headers["Cookie"] = cookieHeader;
                }

                byte[] bytes = Encoding.UTF8.GetBytes(jsonBody);
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);

                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                    ReadResponse(resp, result, container, url, name);
            }
            catch (System.Net.WebException we)
            {
                var resp = we.Response as System.Net.HttpWebResponse;
                if (resp != null) ReadResponse(resp, result, container, url, name);
                else result.networkError = we.Message;
            }
            catch (System.Exception e)
            {
                result.networkError = e.Message;
            }
            finally { done = true; }
        });

        while (!done) yield return null;
    }

    /// <summary>讀取回應內容，並從三種來源嘗試取出 Token</summary>
    private static void ReadResponse(System.Net.HttpWebResponse resp, HttpResponse result,
                                     System.Net.CookieContainer container, string url, string name)
    {
        result.status = (int)resp.StatusCode;

        try
        {
            using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
                result.text = sr.ReadToEnd();
        }
        catch { }

        // 來源 1：resp.Cookies（掛了 CookieContainer 時由 .NET 解析好）
        try
        {
            if (resp.Cookies != null && resp.Cookies.Count > 0)
            {
                var c = resp.Cookies[name];
                if (c != null) result.capturedToken = c.Value;
            }
        }
        catch { }

        // 來源 2：CookieContainer 內（網域比對可能失敗，所以放第二順位）
        if (string.IsNullOrEmpty(result.capturedToken) && container != null)
        {
            try
            {
                var cs = container.GetCookies(new System.Uri(url));
                foreach (System.Net.Cookie c in cs)
                {
                    if (c.Name == name) { result.capturedToken = c.Value; break; }
                }
            }
            catch { }
        }

        // 來源 3：raw 標頭（掃所有 key，不分大小寫）
        var sb = new StringBuilder();
        try
        {
            foreach (string key in resp.Headers.AllKeys)
            {
                string val = resp.Headers[key];
                sb.AppendLine($"  {key}: {val}");
                if (key != null && key.ToLower().Contains("cookie"))
                {
                    result.setCookie = string.IsNullOrEmpty(result.setCookie)
                        ? val : result.setCookie + "\n" + val;
                }
            }
        }
        catch { }
        result.allHeaders = sb.ToString();
    }

    /// <summary>路線 B：UnityWebRequest（WebGL 用）</summary>
    private IEnumerator UnityHttpPost(string endpoint, string jsonBody, HttpResponse result)
    {
        using (var req = BuildJsonPost(endpoint, jsonBody))
        {
            yield return req.SendWebRequest();

            result.status = (int)req.responseCode;
            result.text = req.downloadHandler != null ? req.downloadHandler.text : "";

            var all = req.GetResponseHeaders();
            if (all != null)
            {
                var sb = new StringBuilder();
                foreach (var kv in all)
                {
                    sb.AppendLine($"  {kv.Key}: {kv.Value}");
                    if (kv.Key != null && kv.Key.ToLower().Contains("cookie"))
                        result.setCookie = kv.Value;
                }
                result.allHeaders = sb.ToString();
            }

            if (req.result == UnityWebRequest.Result.ConnectionError)
                result.networkError = req.error;
        }
    }

    public UnityWebRequest BuildJsonPost(string endpoint, string jsonBody)
    {
        var req = new UnityWebRequest(BASE_URL + endpoint, "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Accept", "application/json");
        req.timeout = REQUEST_TIMEOUT_SEC;

        string cookieHeader = CookieHeaderValue;
        if (!string.IsNullOrEmpty(cookieHeader))
            req.SetRequestHeader("Cookie", cookieHeader);

        return req;
    }

    private void LogOutgoingCookie(string endpoint)
    {
        if (string.IsNullOrEmpty(authToken))
        {
            if (endpoint != AUTH_ENDPOINT)
                Debug.LogWarning($"⚠️ 送往 {endpoint} 時沒有 Token，這一定會 401");
            return;
        }
        Debug.Log($"🍪 送出 Cookie → {cookieName}={Peek(authToken)}（{authToken.Length} 字元）");
    }

    /// <summary>只顯示頭尾片段，方便跟 DevTools 對照又不用整串洗版</summary>
    private static string Peek(string s, int n = 14)
    {
        if (string.IsNullOrEmpty(s)) return "(空)";
        if (s.Length <= n * 2 + 5) return s;
        return s.Substring(0, n) + "..." + s.Substring(s.Length - n);
    }

    #endregion

    #region 共用：解析

    private JObject ParseEnvelope(string text, out string code, out string msg)
    {
        code = null;
        msg = null;
        if (string.IsNullOrEmpty(text)) return null;

        JObject root;
        try { root = JObject.Parse(text); }
        catch { return null; }

        code = root["processResultCode"]?.ToString();
        msg = root["processResultMsg"]?.ToString() ?? root["message"]?.ToString();
        if (string.IsNullOrEmpty(code)) code = root["error"]?.ToString();

        var po = root["processObject"];
        if (po == null || po.Type == JTokenType.Null) return null;

        if (po.Type == JTokenType.String)
        {
            try { return JObject.Parse(po.ToString()); }
            catch { return null; }
        }
        return po as JObject;
    }

    /// <summary>
    /// 從 Set-Cookie 取出 JWT 原字串。
    /// JWT 的字元集是 base64url（A-Z a-z 0-9 - _ .），不含 ; , 空白，
    /// 所以用 [^;,\s]+ 切是安全的。
    /// </summary>
    private bool CaptureToken(HttpResponse res)
    {
        // 優先用 .NET 解析好的 Cookie 物件
        if (!string.IsNullOrEmpty(res.capturedToken))
        {
            authToken = res.capturedToken;
            Debug.Log("📥 Token 來源：CookieContainer");
        }
        else if (!string.IsNullOrEmpty(res.setCookie))
        {
            if (verboseLog)
                Debug.Log($"📥 Set-Cookie 原始內容（前 200 字）:\n{Truncate(res.setCookie, 200)}");

            var m = Regex.Match(res.setCookie, Regex.Escape(cookieName) + @"=([^;,\s]+)");
            if (!m.Success)
            {
                Debug.LogError($"❌ Set-Cookie 中找不到 {cookieName}");
                Debug.LogError($"完整回應標頭:\n{res.allHeaders}");
                return false;
            }
            authToken = m.Groups[1].Value;
            Debug.Log("📥 Token 來源：raw Set-Cookie 標頭");
        }
        else if (TryFindTokenInBody(res.text))
        {
            Debug.Log("📥 Token 來源：回應 body");
        }
        else
        {
            Debug.LogError("❌ 所有來源都取不到 Token（resp.Cookies / CookieContainer / raw 標頭 / body）");
            Debug.LogError($"完整回應標頭:\n{(string.IsNullOrEmpty(res.allHeaders) ? "(空)" : res.allHeaders)}");
            Debug.LogError("→ 若標頭清單裡確實沒有 Set-Cookie，代表後端這次沒下發 Cookie，" +
                           "可先用 debugTokenOverride 貼上 DevTools 的 Token 繼續測後面的流程");
            return false;
        }

        int dots = authToken.Split('.').Length - 1;
        Debug.Log($"🔑 取得 Token：{Peek(authToken)}（{authToken.Length} 字元，{dots} 個 '.'）");

        if (dots != 2)
        {
            Debug.LogError($"❌ Token 格式不對，JWT 應該有 2 個 '.'，實際 {dots} 個 → 被截斷或抓錯範圍");
            return false;
        }

        var ma = Regex.Match(res.setCookie ?? "", @"Max-Age=(\d+)", RegexOptions.IgnoreCase);
        if (ma.Success && int.TryParse(ma.Groups[1].Value, out int sec))
        {
            cookieExpireUnixTime = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() + sec;
            Debug.Log($"🍪 Session 有效期 {sec / 3600f:F1} 小時");
        }

        return true;
    }

    /// <summary>
    /// 最後手段：有些後端會把 Token 也放進回應 body。
    /// 直接用「三段 base64url、以 eyJ 開頭」的特徵在整份 body 裡找 JWT。
    /// </summary>
    private bool TryFindTokenInBody(string body)
    {
        if (string.IsNullOrEmpty(body)) return false;

        var m = Regex.Match(body, @"eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+");
        if (!m.Success) return false;

        authToken = m.Value;
        return true;
    }

    public bool IsSessionExpired()
    {
        if (cookieExpireUnixTime <= 0) return false;
        return System.DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= cookieExpireUnixTime - 60;
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "(空)";
        return s.Length <= max ? s : s.Substring(0, max) + "...(略)";
    }

    #endregion

    #region Step 1: 驗證 Kiosk Code

    /// <summary>
    /// POST /auth/fixed  Body: { companyId, quickLoginCode }
    /// 成功：processResultCode == "AuthSuccess"，並從 Set-Cookie 取出 JWT
    /// </summary>
    public IEnumerator AuthKiosk()
    {
        isAuthSuccess = false;
        lastErrorCode = null;
        lastErrorMessage = null;
        authToken = null;
        UnityWebRequest.ClearCookieCache();

        // 除錯捷徑：直接使用手動貼上的 Token，跳過驗證
        if (!string.IsNullOrEmpty(debugTokenOverride))
        {
            authToken = debugTokenOverride.Trim();
            isAuthSuccess = true;
            Debug.LogWarning(
                $"⚠️ 使用 debugTokenOverride 跳過驗證：{Peek(authToken)}（{authToken.Length} 字元）\n" +
                "   這是除錯捷徑。若後續出現 StreamingNotFoundException，請清空此欄位。");
            yield break;
        }

        var body = new JObject
        {
            ["companyId"] = companyId,
            ["quickLoginCode"] = quickLoginCode
        };

        var res = new HttpResponse();
        yield return PostJson(AUTH_ENDPOINT, body.ToString(Newtonsoft.Json.Formatting.None), res);

        if (res.networkError != null)
        {
            lastErrorCode = "ERR_AUTH_NETWORK";
            lastErrorMessage = res.networkError;
            Debug.LogError($"❌ 驗證連線錯誤: {res.networkError}");
            yield break;
        }

        // 驗證這一次一定把完整回應印出來，Token 只可能藏在標頭或 body 裡
        if (verboseLog)
        {
            Debug.Log($"📋 /auth 回應標頭:\n{(string.IsNullOrEmpty(res.allHeaders) ? "(空)" : res.allHeaders)}");
            Debug.Log($"📋 /auth 回應內容:\n{Truncate(res.text, 1500)}");
        }

        ParseEnvelope(res.text, out string code, out string msg);

        if (res.IsSuccess && code == "AuthSuccess")
        {
            if (!CaptureToken(res))
            {
                lastErrorCode = "ERR_AUTH_NO_TOKEN";
                lastErrorMessage = "驗證成功但取不到有效 Token";
                yield break;
            }

            isAuthSuccess = true;
            Debug.Log("✅ Kiosk Code 驗證成功 (AuthSuccess)");
            yield break;
        }

        if (res.status == 403 && code == "NoScopeStreamingFound")
        {
            lastErrorCode = code;
            lastErrorMessage = "找不到符合的 Kiosk Code（代碼錯誤）";
        }
        else if (res.status == 403 && code == "NoCertifiableStreamingFound")
        {
            lastErrorCode = code;
            lastErrorMessage = "目前沒有可使用的 Streaming（連線滿額）";
        }
        else if (string.IsNullOrEmpty(code))
        {
            lastErrorCode = "ERR_AUTH_NOT_JSON";
            lastErrorMessage = $"回應不是預期的 JSON（HTTP {res.status}），請檢查 BASE_URL / 端點";
        }
        else
        {
            lastErrorCode = "ERR_AUTH_999";
            lastErrorMessage = msg ?? "未知錯誤";
        }

        Debug.LogError($"❌ Kiosk 驗證失敗 [{res.status}] {lastErrorCode}: {lastErrorMessage}");
        Debug.LogError($"回應內容:\n{Truncate(res.text, 500)}");
    }

    #endregion

    #region Step 2: 建立 Streaming Session

    /// <summary>POST /streaming/start（無 Body，靠 Cookie 驗證）</summary>
    public IEnumerator StartStreaming()
    {
        isStreamingStarted = false;

        var res = new HttpResponse();
        yield return PostJson(STREAMING_START_ENDPOINT, "{}", res);

        if (res.networkError != null)
        {
            lastErrorCode = "ERR_STREAMING_NETWORK";
            lastErrorMessage = res.networkError;
            Debug.LogError($"❌ Streaming 連線錯誤: {res.networkError}");
            yield break;
        }

        JObject po = ParseEnvelope(res.text, out string code, out string msg);

        if (res.IsSuccess && code == "StartStreamingSuccess")
        {
            streamingSettings = new StreamingSettings
            {
                backgroundtype = po?["backgroundtype"]?.ToString(),
                backgroundColorCode = po?["backgroundColorCode"]?.ToString(),
                avatarLogoFileUrl = po?["avatarLogoFileUrl"]?.ToString(),
                avatarDefaultLanguage = po?["avatarDefaultLanguage"]?.ToString(),
                avatarLanguages = po?["avatarLanguages"]?.ToObject<string[]>()
            };

            isStreamingStarted = true;
            Debug.Log($"✅ Streaming Session 建立成功，預設語言: {streamingSettings.avatarDefaultLanguage}");
            yield break;
        }

        if (res.status == 401)
        {
            lastErrorCode = "ERR_STREAMING_401";
            lastErrorMessage =
                "Token 認證失敗。請對照上面兩行 log：\n" +
                "   ① 『🔑 取得 Token』的長度／頭尾，跟 DevTools 的 Set-Cookie 是否一致\n" +
                "   ② 『🍪 送出 Cookie』是否跟 ① 相同\n" +
                "   若兩者一致仍 401，代表這組 Token 不被 /streaming/start 接受，需與後端確認";
        }
        else if (res.status == 400 && code == "StartStreamingError")
        {
            lastErrorCode = "ERR_STREAMING_001";
            lastErrorMessage =
                (msg ?? "串流啟用失敗") + "\n" +
                "   Token 已被接受（不是認證問題），是後端不讓這個帳號現在開串流。常見原因：\n" +
                "   ① 這組 Token 是從瀏覽器 session 複製來的，那個 session 已經佔用了串流名額\n" +
                "      → 關掉瀏覽器的 KIOSK 分頁，清空 debugTokenOverride，讓 Unity 自己跑一次驗證\n" +
                "   ② 同一帳號的串流名額已滿或尚未釋放 → 等一下再試\n" +
                "   ③ 這組帳號沒有開串流的權限 → 需與後端確認";
        }
        else if (code == "StreamingNotFoundException")
        {
            lastErrorCode = "ERR_STREAMING_NOT_FOUND";
            lastErrorMessage =
                (msg ?? "串流資料不存在") + "\n" +
                "   Token 本身有效，但它指向的 streamingUseCode 在後端已不存在。\n" +
                "   代表這是一組「過期的配額」——最常見於從瀏覽器複製 Token，\n" +
                "   而瀏覽器那邊的 session 已經結束、串流資料被釋放。\n" +
                "   → 請清空 debugTokenOverride，讓 Unity 自己跑 /auth/fixed 取得新 Token。";
        }
        else
        {
            lastErrorCode = res.status == 400 ? "ERR_STREAMING_001" : "ERR_STREAMING_999";
            lastErrorMessage = msg ?? code ?? "未知錯誤";
        }

        Debug.LogError($"❌ Streaming 啟動失敗 [{res.status}] {lastErrorCode}: {lastErrorMessage}");
        Debug.LogError($"回應內容:\n{Truncate(res.text, 500)}");
    }

    #endregion

    #region Step 3: 取得 TURN Server

    /// <summary>POST /streaming/getTurnInformation（Body: {}，靠 Cookie 驗證）</summary>
    public IEnumerator GetTurnInformation()
    {
        isTurnReady = false;

        var res = new HttpResponse();
        yield return PostJson(TURN_ENDPOINT, "{}", res);

        if (res.networkError != null)
        {
            Debug.LogError($"❌ TURN 連線錯誤: {res.networkError}");
            yield break;
        }

        JObject po = ParseEnvelope(res.text, out string code, out string msg);

        if (!res.IsSuccess)
        {
            Debug.LogError($"❌ 取得 TURN 資訊失敗 [{res.status}] {code}: {msg}");
            Debug.LogError($"回應內容:\n{Truncate(res.text, 500)}");
            yield break;
        }

        if (po == null || po["turn_information"] == null)
        {
            Debug.LogError($"❌ TURN 回應解析失敗（processResultCode={code}）\n{Truncate(res.text, 800)}");
            yield break;
        }

        turnInfo = po["turn_information"].ToObject<TurnInformation>();
        accessToken = po["access_token"]?.ToString();

        if (turnInfo?.urls == null || turnInfo.urls.Length == 0)
        {
            Debug.LogError("❌ TURN urls 為空");
            yield break;
        }

        isTurnReady = true;
        Debug.Log($"✅ TURN 資訊取得成功，urls: {string.Join(", ", turnInfo.urls)}");
        Debug.Log("🔑 Access Token 已取得（建立 Offer 使用）");
    }

    #endregion
}
