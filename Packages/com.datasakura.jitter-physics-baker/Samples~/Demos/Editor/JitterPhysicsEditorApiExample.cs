using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Editor.Api;

namespace DataSakura.JitterPhysics.Samples.Editor
{
    /// <summary>Minimal standalone and externally managed calls for consumer editor adapters.</summary>
    public static class JitterPhysicsEditorApiExample
    {
        /// <summary>Validates a standalone level using the id owned by its component.</summary>
        public static JitterPhysicsEditorResult ValidateStandalone(JitterPhysicsLevel level)
        {
            return JitterPhysicsEditorApi.Validate(level, JitterPhysicsLevelIdBinding.Standalone);
        }

        /// <summary>Bakes with an id explicitly owned by another editor tool such as NPI.</summary>
        public static JitterPhysicsEditorResult BakeManaged(
            JitterPhysicsLevel level,
            string owner,
            string levelId)
        {
            return JitterPhysicsEditorApi.Bake(
                level,
                JitterPhysicsLevelIdBinding.External(owner, levelId));
        }
    }
}
