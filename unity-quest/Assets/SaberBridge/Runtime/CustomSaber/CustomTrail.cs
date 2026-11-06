using UnityEngine;

namespace CustomSaber
{
    public enum ColorType
    {
        LeftSaber,
        RightSaber,
        CustomColor,
    }

    // Same name and fields as the CustomSabers trail, so trail settings inside .saber files can be read.
    public class CustomTrail : MonoBehaviour
    {
        public Transform PointStart;
        public Transform PointEnd;
        public Material TrailMaterial;
        public ColorType colorType = ColorType.CustomColor;
        public Color TrailColor = Color.white;
        public Color MultiplierColor = Color.white;
        public int Length = 20;
    }
}
