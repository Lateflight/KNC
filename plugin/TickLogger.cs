using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP;


namespace KNC
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class TickLogger : MonoBehaviour
    {
        void FixedUpdate()
        {
            Debug.Log("[KNC] " + Planetarium.GetUniversalTime() + " " + TimeWarp.fixedDeltaTime);
        }
    }
}
