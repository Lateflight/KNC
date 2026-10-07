using System;
using KSP;
using UnityEngine;

namespace KNC
{
    public class AccelerometerModel : ModuleGNCManager
    {
        bool firsttick = true;
        Vector3d acceleration = Vector3d.zero;

        [KSPField(isPersistant = false, guiActive = true, guiName = "Acceleration X", guiFormat = "F3", guiUnits = "m/s²")]
        double xacc = 0;
        [KSPField(isPersistant = false, guiActive = true, guiName = "Acceleration Y", guiFormat = "F3", guiUnits = "m/s²")]
        double yacc = 0;
        [KSPField(isPersistant = false, guiActive = true, guiName = "Acceleration Z", guiFormat = "F3", guiUnits = "m/s²")]
        double zacc = 0;


        
        Vector3d v_prev = Vector3d.zero;
        double last_time;
        Vector3d g_sum = Vector3d.zero;
        int n = 0;
        bool was_rotating;
        CelestialBody last_body;
        private float noise_x;
        private float noise_y;
        private float noise_z;

        
        void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || part == null || part.rb == null)
                return;
            string powerStatus = "";
            bool powered = resHandler.UpdateModuleResourceInputs(ref powerStatus, 1.0, 0.9, true);
            if (!powered)
            {
                firsttick = true;   // the sensor was off: start a fresh baseline when power comes back
                return;
            }
            CelestialBody body = vessel.mainBody;

            if (vessel.packed || vessel.precalc.isEasingGravity)
            {
                firsttick = true;   // on rails, or KSP still ramping gravity up after a load
                return;
            }

            if (body.inverseRotation != was_rotating || body != last_body)
            {
                firsttick = true;   // KSP just changed what v is measured against
            }
            was_rotating = body.inverseRotation;
            last_body = body;

            Vector3d v = (Vector3d)part.rb.velocity + Krakensbane.GetFrameVelocity();
            double time = Planetarium.GetUniversalTime();
            Vector3d pos = part.rb.worldCenterOfMass;

            if (firsttick)
            {
                // (Re)establish the baseline
                firsttick = false;
                v_prev = v;
                last_time = time;
                g_sum = Vector3d.zero;
                n = 0;
            }
            else if (n > 0 && time - last_time >= 1.0 / (SampleRate*Time.fixedDeltaTime/0.02) - 1e-9) //run the calculation if the time since the last sample is greater than or equal to the sample period, with a small epsilon to account for floating point errors
            {
                // dv/dt minus the mean "field" acceleration over the interval = specific force
                acceleration = (v - v_prev) / (time - last_time) - g_sum / n;
                //Implement later: noise model:
                //Candidate models: Gauss Markov, Gauss-Markov, or white noise. For now, just add a random value to each axis based on the noise_value field.
                noise_x = UnityEngine.Random.Range(-stderr.x, stderr.x);
                noise_y = UnityEngine.Random.Range(-stderr.y, stderr.y);
                noise_z = UnityEngine.Random.Range(-stderr.z, stderr.z);
                Debug.Log("[KNC AccelerometerModel] Noise values: " + stderr.x + " " + stderr.y + " " + stderr.z);
                Vector3 fBody = part.transform.InverseTransformDirection((Vector3)acceleration);
                xacc = fBody.x+noise_x;
                yacc = fBody.y+noise_y;
                zacc = fBody.z+noise_z;

                v_prev = v;
                last_time = time;
                g_sum = Vector3d.zero;
                n = 0;
            }

            g_sum += FlightGlobals.getGeeForceAtPosition(pos, body)
                   + FlightGlobals.getCoriolisAcc(v, body)
                   + FlightGlobals.getCentrifugalAcc(pos, body);
            n++;
        }
    }
}