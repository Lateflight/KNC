using UnityEngine;
using KSP;
//A joke mod that kills Kenny Kerman every 5 seconds in flight mode.
//A reference to the South Park episode "Kenny Dies" where the characters often say "Oh my God, they killed Kenny! You bastards!" when Kenny Kerman dies.
//Future plan: Limit the amounts of Kenny deaths to one per save file.
//Note: an easter egg, not a serious mod. If you want to use it, you can, but don't expect it to be useful in any way. It's just for fun.
namespace KNC
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]

    public class YouBastards : MonoBehaviour //You Bastards is a reference to the South Park episode "Kenny Dies" where the characters often say "Oh my God, they killed Kenny! You bastards!" when Kenny Kerman dies.
    {

        double death_counter = Planetarium.GetUniversalTime();
        bool killkenny = true;
        void FixedUpdate()
        {
            if (killkenny)
            {
                Vessel activeVessel = FlightGlobals.ActiveVessel;

                if (activeVessel != null)
                {
                    if (Planetarium.GetUniversalTime() - death_counter > 5)
                    {

                        foreach (ProtoCrewMember crewMember in activeVessel.GetVesselCrew())
                        {
                            string crewname = crewMember.name;
                            if (crewname == "Kenny Kerman")
                            {
                                crewMember.Die(); 
                                
                                Debug.Log("Oh my God, they killed Kenny!"); //Oh my God, they killed Kenny! is a reference to the South Park episode "Kenny Dies" where the characters often say "Oh my God, they killed Kenny! You bastards!" when Kenny Kerman dies.
                                Debug.Log("You Bastards!"); //You Bastards! is a reference to the South Park episode "Kenny Dies" where the characters often say "Oh my God, they killed Kenny! You bastards!" when Kenny Kerman dies.
                                killkenny = false; // killkenny is a reference to the South Park episode "Kenny Dies" where the characters often say "Oh my God, they killed Kenny! You bastards!" when Kenny Kerman dies.
                                break;
                            }


                        }
                        death_counter = Planetarium.GetUniversalTime(); // reset the death counter to the current universal time, so that Kenny Kerman will be killed again in 5 seconds.

                    }
                }

            }
        }
    }
}