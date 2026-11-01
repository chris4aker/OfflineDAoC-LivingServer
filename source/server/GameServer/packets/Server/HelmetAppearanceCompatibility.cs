using System.Collections.Generic;

namespace DOL.GS.PacketHandler
{
    /// <summary>
    /// Presentation-only compatibility for helmet variants that hide the wearer's face in the legacy client.
    /// Item templates, inventory records, and armor properties retain their original extension.
    /// </summary>
    public static class HelmetAppearanceCompatibility
    {
        // Every client helmet object built on the Hibernian "Hib Helm 3" mesh (items.csv 407, H_helm3,
        // Head # 4, alternates 398/404): the scale coif 840, the studded/amber cailiocht cap 827, the
        // leather helm 440 and their guard/possessed/good variants. They render correctly as extension 0,
        // while the extension 2 and 3 variants hide the entire face.
        private static readonly HashSet<int> HibHelm3Models =
        [
            440, 827, 837, 840, 1203, 1207, 1211,
            2769, 2775, 2781, 2787, 2831, 2837, 2843, 2849
        ];

        // The Hibernian "Hib Helm 1" mesh (items.csv 409, H_helm1, Head # 2, alternates 400/406):
        // the Celtic scale helm 838 (e.g. Animalbound Osnadur Tha Coif) and its variants. Extension 2
        // renders invisible; extension 0 is the same helm without the hidden head.
        private static readonly HashSet<int> HibHelm1Models = [438, 835, 838, 1201, 1205, 1209];

        // The Norse "NHelm3" mesh (items.csv 389, Head # 4, alternates 395/392): the leather cap 337
        // (e.g. rawhide starklaedar cap), the chain helm 834 (e.g. fine alloy heavy starkakedja helm)
        // and their variants. Extensions 2 and 3 render invisible on the wearer.
        private static readonly HashSet<int> NorseHelm3Models =
            [337, 831, 834, 1216, 1219, 1223, 1227, 2862, 2868, 2874, 2880];

        // The Norse "Tiara - Mid" mesh (items.csv 536, nt_hat06, Head # 1, alternates 536/541): the war
        // circlet 1291 (e.g. fine alloy superior war circlet) and its variants. Extension 3 hides the head.
        private static readonly HashSet<int> NorseTiaraModels = [1291, 4169, 4228, 4229, 4230, 4231, 4465];

        // The Albion "helm3" mesh (items.csv 401, Head # 4): the chain coif 63 (e.g. alloy mail coif, seen with no face
        // on an Albion gamebot, owner 2026-10-29) and its variants. Like the other Head # 4 meshes, extensions 2 and 3.
        private static readonly HashSet<int> AlbHelm3Models =
            [63, 1231, 1234, 1238, 1242, 1244, 2716, 2750, 2800, 2812, 3461];

        // The Albion "helm1" mesh (items.csv 403, Head # 2): the leather helm 62 (e.g. cured siluric leather helm, seen
        // with no face, owner 2026-10-29), the cloth helm 823 and the chain helm 1236 (both stand-ins of HelmetModels)
        // and their variants. Like the Hibernian Head # 2 mesh, extension 2.
        private static readonly HashSet<int> AlbHelm1Models =
            [62, 64, 94, 823, 824, 1229, 1230, 1233, 1236, 1241, 2710, 2722, 2744, 2756, 2806, 2818];

        public static byte VisibleExtension(int slot, int model, byte extension)
        {
            if (slot != (int)eInventorySlot.HeadArmor)
                return extension;

            if (HibHelm3Models.Contains(model) && (extension == 2 || extension == 3))
                return 0;

            if (HibHelm1Models.Contains(model) && extension == 2)
                return 0;

            if (NorseHelm3Models.Contains(model) && (extension == 2 || extension == 3))
                return 0;

            if (NorseTiaraModels.Contains(model) && extension == 3)
                return 0;

            if (AlbHelm3Models.Contains(model) && (extension == 2 || extension == 3))
                return 0;

            if (AlbHelm1Models.Contains(model) && extension == 2)
                return 0;

            return extension;
        }
    }
}
