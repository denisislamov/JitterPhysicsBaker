using DataSakura.JitterPhysics.Authoring;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Baking
{
    /// <summary>
    /// Builds the stable key that identifies one collider inside its static body.
    /// <para>
    /// The key decides shape order in the artifact, so it has to be derived from something
    /// that does not change between two bakes of an unchanged scene. Instance ids, hash map
    /// enumeration order and <c>FindObjectsByType</c> ordering all fail that requirement:
    /// they are stable within a session and arbitrary across sessions, which produces an
    /// artifact whose hash changes for no visible reason.
    /// </para>
    /// <para>
    /// What is used instead is the collider's structural position: the path from the body
    /// root, the sibling index of every step, the index of the component on its object and
    /// the collider type. Two colliders can only collide in this key if they occupy the same
    /// place in the hierarchy, which is impossible.
    /// </para>
    /// </summary>
    public static class JitterPhysicsColliderKey
    {
        /// <summary>
        /// Returns the canonical key of <paramref name="collider"/> relative to
        /// <paramref name="bodyRoot"/>.
        /// </summary>
        public static string Build(Transform bodyRoot, Collider collider)
        {
            return Authoring.JitterPhysicsStableColliderKey.Build(bodyRoot, collider);
        }
    }
}
