using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DisplayActitive : MonoBehaviour
{
    void Start()
    {
        Display.displays[0].Activate();
        Display.displays[1].Activate();  
        Display.displays[2].Activate();
        Display.displays[3].Activate();
        
    }

    // Update is called once per frame
    void Update()
    {
    }
}
