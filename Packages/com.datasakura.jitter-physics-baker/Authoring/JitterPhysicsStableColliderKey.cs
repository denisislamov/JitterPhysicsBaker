using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DataSakura.JitterPhysics.Authoring
{
    /// <summary>Builds the stable structural key of a collider inside one static body.</summary>
    public static class JitterPhysicsStableColliderKey
    {
        private const char StepSeparator = '/';

        /// <summary>Returns a canonical hierarchy/component key without using instance IDs.</summary>
        public static string Build(Transform bodyRoot, Collider collider)
        {
            var builder = new StringBuilder(64);
            AppendPath(builder, bodyRoot, collider.transform);
            builder.Append(StepSeparator)
                .Append(TypeTag(collider))
                .Append('#')
                .Append(ComponentIndex(collider).ToString(CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static void AppendPath(StringBuilder builder, Transform bodyRoot, Transform target)
        {
            var steps = new List<Transform>();
            for (Transform current = target; current != null && current != bodyRoot; current = current.parent)
            {
                steps.Add(current);
            }

            for (int index = steps.Count - 1; index >= 0; index--)
            {
                Transform step = steps[index];
                builder.Append(StepSeparator)
                    .Append(step.GetSiblingIndex().ToString(CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(Sanitize(step.name));
            }
        }

        private static int ComponentIndex(Collider collider)
        {
            Collider[] colliders = collider.gameObject.GetComponents<Collider>();
            for (int index = 0; index < colliders.Length; index++)
            {
                if (ReferenceEquals(colliders[index], collider)) return index;
            }

            return 0;
        }

        private static string TypeTag(Collider collider)
        {
            switch (collider)
            {
                case BoxCollider _: return "box";
                case SphereCollider _: return "sphere";
                case CapsuleCollider _: return "capsule";
                case MeshCollider _: return "mesh";
                default: return "unsupported";
            }
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";

            var builder = new StringBuilder(name.Length);
            for (int index = 0; index < name.Length && builder.Length < 48; index++)
            {
                char character = char.ToLowerInvariant(name[index]);
                bool allowed = (character >= 'a' && character <= 'z')
                    || (character >= '0' && character <= '9') || character == '_';
                builder.Append(allowed ? character : '_');
            }

            return builder.Length == 0 ? "unnamed" : builder.ToString();
        }
    }
}
