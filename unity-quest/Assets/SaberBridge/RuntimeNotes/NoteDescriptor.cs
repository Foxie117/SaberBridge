using UnityEngine;

namespace CustomNotes
{
    // Stand-in for Custom Notes' NoteDescriptor (PC mod). Same assembly, namespace and field names, so the data in
    // .bloq files can be read; it does nothing at runtime.
    public class NoteDescriptor : MonoBehaviour
    {
        public string NoteName = "Note";
        public string AuthorName = "Author";
        public string Description = string.Empty;
        public Texture2D Icon;
        public bool DisableBaseNoteArrows = false;
        public bool UsesNoteColor = false;
        public float NoteColorStrength = 1.0f;
    }
}
