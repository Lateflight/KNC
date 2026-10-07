using System;
using UnityEngine;
using KSP;
using System.IO;
namespace KNC
{
    public class ModuleNotGncSensor : PartModule
    {
        public override string GetModuleDisplayName()
        {
            return "Not A GNC Sensor";
        }
        public override string GetInfo()
        {
            
            return $"<b>Cannot be used as a GNC sensor, because it's a precision tool for scientific endeavours, and is too slow for proper GNC applications. </b>";
        }
    }
}