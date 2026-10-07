using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KNC
{
    public class ModuleKncToy : PartModule
    {
        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Gain")]
        public float gain;

    }
}
