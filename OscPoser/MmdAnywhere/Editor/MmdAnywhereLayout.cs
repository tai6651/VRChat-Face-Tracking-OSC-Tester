using System.Linq;

namespace OscTester
{
    // Shared by the Windows sender and Unity installer. Payload indices map to physical OSC keys.
    internal static class MmdAnywhereLayout
    {
        public const int WireSize = 102;
        public const string EnableParameter = "MmdAnywhere/BodyStreamEnabled";
        public const int Center = 128;
        public const int SignedSteps = 111;
        public const int ZeroGap = 1;
        public const int MinDataByte = Center - SignedSteps - ZeroGap;
        public const int MissingByteThreshold = MinDataByte / 2;
        public const float MuscleRange = 3;

        public static readonly int[] Muscles = Enumerable.Range(0, 95).Except(new[]
        {
            6, 7, 8, 19, 20, 28, 36
        }).ToArray();
        // One value drives the second and third curl joints of each finger.
        public static readonly int[] SharedCurlStarts = { 57, 61, 65, 69, 73, 77, 81, 85, 89, 93 };
        // Both eyes share pitch and yaw. The Unity horizontal axes have opposite signs.
        public static readonly int[] MuscleCarriers = Muscles.Except(SharedCurlStarts.Select(i => i + 1))
            .Except(new[] { 17, 18 }).ToArray();
        public static readonly int[] ReservedKeys = { 1, 3, 5, 7, 9, 16, 17, 38, 88, 90 };
        public static readonly int[] RootKeys = { 62, 65, 80, 91, 92, 93, 94 };
        public const int RootXExtraKey = 98;
        public const int RootZExtraKey = 99;
        public const int HorizontalFineMin = 16;
        public const int HorizontalFineMax = 240;
        public const int HorizontalFineSpan = HorizontalFineMax - HorizontalFineMin;
        public static readonly int[] RemainingKeys = Enumerable.Range(0, WireSize)
            .Except(ReservedKeys).Except(RootKeys).Except(new[] { RootXExtraKey, RootZExtraKey }).ToArray();
        public static readonly string[] RootProperties = { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
        public const float TranslationRange = 5;
        public const int FineTranslationSteps = 80;
        public static float FineTranslationRange(int axis) => axis == 1 ? .35f : 1.25f;
        public static string TranslationParameter(int axis) => "MmdAnywhere/" + RootProperties[axis];
        public static readonly string[] FaceNames =
        {
            "Left eyelid", "Right eyelid", "Jaw open", "Mouth stretch", "Mouth round", "Smile", "Pupil constriction"
        };
        public static string FaceParameter(int index) => "MmdAnywhere/Face" + index.ToString("D2");
        public static int MuscleKey(int index) => RemainingKeys[index];
        public static int FaceKey(int index) => RemainingKeys[MuscleCarriers.Length + index];
    }
}
