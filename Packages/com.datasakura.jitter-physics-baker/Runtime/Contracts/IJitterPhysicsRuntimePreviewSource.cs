using System.Collections.Generic;

namespace DataSakura.JitterPhysics.Contracts
{
    /// <summary>
    /// Supplies geometry from an active runtime physics world to editor-only diagnostics.
    /// </summary>
    /// <remarks>
    /// The contract deliberately exposes artifact records instead of Jitter2 types. Projects
    /// can therefore keep their Jitter2 assembly optional, while the editor package remains
    /// importable without it. Implementations must copy what the active world actually loaded;
    /// they must not rebuild records from Unity colliders for the preview.
    /// </remarks>
    public interface IJitterPhysicsRuntimePreviewSource
    {
        /// <summary>The level represented by the active world.</summary>
        string PhysicsPreviewLevelId { get; }

        /// <summary>True only after the runtime world has been built successfully.</summary>
        bool IsPhysicsPreviewReady { get; }

        /// <summary>Copies the geometry currently represented by the active runtime world.</summary>
        /// <param name="destination">Collection that receives immutable preview records.</param>
        void CopyPhysicsPreviewBodies(ICollection<PhysicsBodyRecord> destination);
    }
}
