using System;
using UnityEngine;

namespace KNC
{
	public class ModuleGNCManager : PartModule
    {

        public enum GNCSensorType{
            GNC_ACC_3DOF
        }

        public override string GetModuleDisplayName()
        {
            return "GNC Sensor";
        }
        [KSPField] public double SampleRate = 50;
        
        [KSPField] public Vector3 stderr = Vector3.zero; // X, Y, Z (KSPField can't read arrays; in the .cfg write  stderr = 0, 1, 0  with no braces). Number = enabled and of that value, 0 = disabled. NOTE FOR FUTURE: If Kerbalism is installed, should use the Kerbalism magnetic field model. If KSPIE is intalled, should use the KSPIE magnetic field model. If both are installed, should use the Kerbalism magnetic field model. Default is 0 on all. See individual component model for more information on how to set the noise values.
        [KSPField] public GNCSensorType sensorType = GNCSensorType.GNC_ACC_3DOF;
        public override string GetInfo()
        {
            if (sensorType == GNCSensorType.GNC_ACC_3DOF)
            {
                string info = $"A fast, albeit <color=#FF5F15>noisy</color>, 3 degrees of freedom accelerometer. Best used for short intervals and should be often corrected.\n\n<color=#00FFFF>Returns acceleration data in m/s² in local body coordinates.</color>";
                info += $"\n\n<b>Sample rate:</b> {Math.Round(SampleRate/(Time.fixedDeltaTime/0.02), 2)} Hz"; // fixed: use Math.Round and closed interpolation
                info += $"\n<b>Noise in X:</b> {stderr.x} m/s²";
                info += $"\n<b>Noise in Y:</b> {stderr.y} m/s²";
                info += $"\n<b>Noise in Z:</b> {stderr.z} m/s²";
                info += "\n\n" + resHandler.PrintModuleResources();
                return info;
            }
            else
            {
                return $"<b>Mystery sensor.</b>";
            }
            //To implement a custom sensor, add a new enum value to GNCSensorType and implement the logic in this method.

        }


    }
}
