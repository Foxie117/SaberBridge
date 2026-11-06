using UnityEngine;

// Same name and fields as the CustomSabers descriptor, so components inside .saber files load and can be read.
public class SaberDescriptor : MonoBehaviour
{
    public string SaberName = "SaberName";
    public string AuthorName = "AuthorName";
    public string Description = "Description";
    public Sprite CoverImage;
}
