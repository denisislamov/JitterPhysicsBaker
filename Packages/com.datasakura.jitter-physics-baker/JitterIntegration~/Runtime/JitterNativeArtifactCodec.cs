using System;
using System.Collections.Generic;
using System.Text;
using DataSakura.JitterPhysics.Contracts;
using Jitter2.LinearMath;
using LegacyArtifact = DataSakura.JitterPhysics.Contracts.PhysicsArtifact;
using LegacyBody = DataSakura.JitterPhysics.Contracts.PhysicsBodyRecord;
using LegacyQuaternion = DataSakura.JitterPhysics.Contracts.PhysicsQuaternion;
using LegacySettings = DataSakura.JitterPhysics.Contracts.PhysicsWorldSettings;
using LegacyShape = DataSakura.JitterPhysics.Contracts.PhysicsShapeRecord;
using LegacyVector = DataSakura.JitterPhysics.Contracts.PhysicsVector3;
using NativeArtifact = DataSakura.JitterPhysics.JitterNative.PhysicsArtifact;
using NativeBody = DataSakura.JitterPhysics.JitterNative.PhysicsBodyRecord;
using NativeCanonicalization = DataSakura.JitterPhysics.JitterNative.PhysicsCanonicalization;
using NativeSettings = DataSakura.JitterPhysics.JitterNative.PhysicsWorldSettings;
using NativeShape = DataSakura.JitterPhysics.JitterNative.PhysicsShapeRecord;
#if !DATASAKURA_SERVER_GLOBAL_REAL
using Real = System.Single;
#endif

namespace DataSakura.JitterPhysics.JitterNative.Codec
{
    /// <summary>Typed result of decoding a Jitter-native artifact.</summary>
    public readonly struct PhysicsArtifactResult
    {
        private PhysicsArtifactResult(NativeArtifact artifact, PhysicsArtifactError error)
        {
            Artifact = artifact;
            Error = error;
        }

        /// <summary>Decoded artifact, or null on failure.</summary>
        public NativeArtifact Artifact { get; }
        /// <summary>Typed external-input failure.</summary>
        public PhysicsArtifactError Error { get; }
        /// <summary>Whether decoding and validation succeeded.</summary>
        public bool Succeeded => !Error.IsError;

        internal static PhysicsArtifactResult Success(NativeArtifact artifact) =>
            new PhysicsArtifactResult(artifact, default);

        internal static PhysicsArtifactResult Failure(PhysicsArtifactError error) =>
            new PhysicsArtifactResult(null, error);
    }

    /// <summary>
    /// Schema-one codec whose authoritative in-memory graph uses Jitter math types and f32 Real.
    /// </summary>
    public static class PhysicsArtifactCodec
    {
        private static readonly byte[] Magic = { 0x4A, 0x50, 0x48, 0x59 };
        private const int CompatibilityIdBytes = 32;
        private const ushort Reserved = 0;
        private const byte SingleThreaded = 0;

        /// <summary>Writes the canonical little-endian schema-one payload.</summary>
        public static byte[] Write(NativeArtifact artifact)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));

            PhysicsArtifactError error = Validate(artifact);
            if (error.IsError)
            {
                throw new ArgumentException(
                    "Refusing to write a non-canonical Jitter-native artifact: " + error,
                    nameof(artifact));
            }

            var writer = new CanonicalWriter(
                128 + artifact.Bodies.Count * 96 + artifact.ShapeCount * 64
                + artifact.VertexCount * 12 + artifact.TriangleCount * 12);
            writer.WriteBytes(Magic);
            writer.WriteUInt16((ushort)artifact.SchemaVersion);
            writer.WriteUInt16(Reserved);
            writer.WriteBytes(HexToBytes(artifact.RuntimeCompatibilityId));
            writer.WriteString(artifact.LevelId);

            NativeSettings settings = artifact.WorldSettings;
            writer.WriteJVector(settings.Gravity);
            writer.WriteInt32(settings.TickRate);
            writer.WriteInt32(settings.SubstepCount);
            writer.WriteInt32(settings.SolverIterations);
            writer.WriteInt32(settings.RelaxationIterations);
            writer.WriteByte(settings.AllowDeactivation ? (byte)1 : (byte)0);
            writer.WriteByte(NativeSettings.DeterministicSolveMode);
            writer.WriteByte(SingleThreaded);

            writer.WriteInt32(artifact.Bodies.Count);
            for (int bodyIndex = 0; bodyIndex < artifact.Bodies.Count; bodyIndex++)
            {
                NativeBody body = artifact.Bodies[bodyIndex];
                writer.WriteString(body.SourceId);
                writer.WriteJVector(body.Position);
                writer.WriteJQuaternion(body.Orientation);
                writer.WriteReal(body.Friction);
                writer.WriteReal(body.Restitution);
                writer.WriteInt32(body.Shapes.Count);

                for (int shapeIndex = 0; shapeIndex < body.Shapes.Count; shapeIndex++)
                {
                    WriteShape(writer, body.Shapes[shapeIndex]);
                }
            }

            return writer.ToArray();
        }

        /// <summary>
        /// Strictly decodes schema-one bytes and returns Jitter-native records. During the
        /// compatibility window the mature schema-one parser remains the byte-input boundary;
        /// its fully validated graph is converted once and the bridge is removed in JMP-E07.
        /// </summary>
        public static PhysicsArtifactResult Read(
            byte[] payload,
            string expectedHash = null,
            PhysicsArtifactManifest manifest = null)
        {
            DataSakura.JitterPhysics.Contracts.PhysicsArtifactResult legacy =
                DataSakura.JitterPhysics.ArtifactCodec.PhysicsArtifactReader.Read(
                    payload, expectedHash, manifest);
            if (!legacy.Succeeded)
            {
                return PhysicsArtifactResult.Failure(legacy.Error);
            }

            NativeArtifact artifact = LegacyPhysicsArtifactBridge.FromLegacy(legacy.Artifact);
            PhysicsArtifactError validation = Validate(artifact);
            return validation.IsError
                ? PhysicsArtifactResult.Failure(validation)
                : PhysicsArtifactResult.Success(artifact);
        }

        /// <summary>Validates native records and preserves typed external-input failures.</summary>
        public static PhysicsArtifactError Validate(NativeArtifact artifact)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));

            DataSakura.JitterPhysics.Integration.JitterRuntimeProfileResult profile =
                DataSakura.JitterPhysics.Integration.JitterRuntimeProfile.VerifyCanonicalF32();
            if (!profile.Succeeded) return profile.Error;

            // The conversion is exact f32 field copying. Reusing the mature ordering, limit and
            // mesh validator during the bounded compatibility window prevents two rule sets from
            // drifting while E05 migrates producers and E07 removes the legacy graph.
            LegacyArtifact legacy;
            try
            {
                legacy = LegacyPhysicsArtifactBridge.ToLegacy(artifact);
            }
            catch (ArgumentException exception)
            {
                return new PhysicsArtifactError(
                    PhysicsArtifactErrorCode.InvalidValue,
                    exception.Message,
                    artifact.LevelId);
            }

            return DataSakura.JitterPhysics.ArtifactCodec.PhysicsArtifactValidator.Validate(legacy);
        }

        private static void WriteShape(CanonicalWriter writer, NativeShape shape)
        {
            writer.WriteString(shape.ShapeKey);
            writer.WriteByte((byte)shape.ShapeType);
            writer.WriteJVector(shape.LocalPosition);
            writer.WriteJQuaternion(shape.LocalRotation);

            switch (shape.ShapeType)
            {
                case PhysicsShapeType.Box:
                    writer.WriteJVector(shape.Size);
                    break;
                case PhysicsShapeType.Sphere:
                    writer.WriteReal(shape.Radius);
                    break;
                case PhysicsShapeType.Capsule:
                    writer.WriteReal(shape.Radius);
                    writer.WriteReal(shape.Length);
                    break;
                case PhysicsShapeType.Mesh:
                    writer.WriteInt32(shape.Vertices.Length);
                    for (int index = 0; index < shape.Vertices.Length; index++)
                    {
                        writer.WriteJVector(shape.Vertices[index]);
                    }

                    writer.WriteInt32(shape.Indices.Length);
                    for (int index = 0; index < shape.Indices.Length; index++)
                    {
                        writer.WriteInt32(shape.Indices[index]);
                    }

                    break;
                default:
                    throw new ArgumentException("Unsupported shape type.", nameof(shape));
            }
        }

        private static byte[] HexToBytes(string hex)
        {
            if (hex == null || hex.Length != CompatibilityIdBytes * 2)
            {
                throw new ArgumentException("Runtime compatibility id must contain 64 hex digits.", nameof(hex));
            }

            var bytes = new byte[CompatibilityIdBytes];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = (byte)((Hex(hex[index * 2]) << 4) | Hex(hex[index * 2 + 1]));
            }

            return bytes;
        }

        private static int Hex(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            throw new ArgumentException("Compatibility id is not lowercase hexadecimal.");
        }

        private sealed class CanonicalWriter
        {
            private readonly List<byte> bytes;
            private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

            internal CanonicalWriter(int capacity) => bytes = new List<byte>(capacity);
            internal void WriteByte(byte value) => bytes.Add(value);
            internal void WriteBytes(byte[] value) => bytes.AddRange(value);
            internal void WriteUInt16(ushort value)
            {
                bytes.Add((byte)value);
                bytes.Add((byte)(value >> 8));
            }

            internal void WriteInt32(int value)
            {
                uint bits = unchecked((uint)value);
                bytes.Add((byte)bits);
                bytes.Add((byte)(bits >> 8));
                bytes.Add((byte)(bits >> 16));
                bytes.Add((byte)(bits >> 24));
            }

            internal void WriteReal(Real value) =>
                WriteInt32(BitConverter.SingleToInt32Bits(NativeCanonicalization.CanonicalReal(value)));

            internal void WriteJVector(in JVector value)
            {
                WriteReal(value.X);
                WriteReal(value.Y);
                WriteReal(value.Z);
            }

            internal void WriteJQuaternion(in JQuaternion value)
            {
                WriteReal(value.X);
                WriteReal(value.Y);
                WriteReal(value.Z);
                WriteReal(value.W);
            }

            internal void WriteString(string value)
            {
                byte[] encoded = Utf8.GetBytes(value ?? string.Empty);
                if (encoded.Length > PhysicsArtifactLimits.MaxStringBytes)
                {
                    throw new ArgumentException("String exceeds the artifact UTF-8 limit.", nameof(value));
                }

                WriteUInt16((ushort)encoded.Length);
                WriteBytes(encoded);
            }

            internal byte[] ToArray() => bytes.ToArray();
        }

        // Kept as a first-class primitive even while schema-one Read delegates hostile-input
        // parsing to the mature reader. E06 switches the full decoder to these methods only after
        // byte/schema policy is approved.
        private sealed class CanonicalReader
        {
            private readonly byte[] bytes;
            private int offset;

            internal CanonicalReader(byte[] bytes) =>
                this.bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));

            internal Real ReadReal()
            {
                Require(4);
                int bits = bytes[offset] | bytes[offset + 1] << 8
                    | bytes[offset + 2] << 16 | bytes[offset + 3] << 24;
                offset += 4;
                Real value = BitConverter.Int32BitsToSingle(bits);
                if (!StableMath.IsFinite(value) || NativeCanonicalization.IsNegativeZero(value))
                {
                    throw new ArgumentException("Payload scalar is not finite canonical f32.");
                }

                return value;
            }

            internal JVector ReadJVector() => new JVector(ReadReal(), ReadReal(), ReadReal());

            internal JQuaternion ReadJQuaternion()
            {
                var value = new JQuaternion(ReadReal(), ReadReal(), ReadReal(), ReadReal());
                if (!NativeCanonicalization.IsCanonicalQuaternion(value))
                {
                    throw new ArgumentException("Payload quaternion is not canonical.");
                }

                return value;
            }

            private void Require(int count)
            {
                if (bytes.Length - offset < count) throw new ArgumentException("Payload is truncated.");
            }
        }
    }

    /// <summary>Temporary exact-field bridge for the E04-E07 source migration window.</summary>
    internal static class LegacyPhysicsArtifactBridge
    {
        internal static LegacyArtifact ToLegacy(NativeArtifact artifact)
        {
            var bodies = new List<LegacyBody>(artifact.Bodies.Count);
            for (int index = 0; index < artifact.Bodies.Count; index++)
            {
                NativeBody body = artifact.Bodies[index];
                var shapes = new List<LegacyShape>(body.Shapes.Count);
                for (int shapeIndex = 0; shapeIndex < body.Shapes.Count; shapeIndex++)
                {
                    shapes.Add(ToLegacy(body.Shapes[shapeIndex]));
                }

                bodies.Add(new LegacyBody(
                    body.SourceId,
                    ToLegacy(body.Position),
                    ToLegacy(body.Orientation),
                    body.Friction,
                    body.Restitution,
                    shapes));
            }

            NativeSettings settings = artifact.WorldSettings;
            return new LegacyArtifact(
                artifact.SchemaVersion,
                artifact.RuntimeCompatibilityId,
                artifact.LevelId,
                new LegacySettings(
                    ToLegacy(settings.Gravity),
                    settings.TickRate,
                    settings.SubstepCount,
                    settings.SolverIterations,
                    settings.RelaxationIterations,
                    settings.AllowDeactivation),
                bodies);
        }

        internal static NativeArtifact FromLegacy(LegacyArtifact artifact)
        {
            var bodies = new List<NativeBody>(artifact.Bodies.Count);
            for (int index = 0; index < artifact.Bodies.Count; index++)
            {
                LegacyBody body = artifact.Bodies[index];
                var shapes = new List<NativeShape>(body.Shapes.Count);
                for (int shapeIndex = 0; shapeIndex < body.Shapes.Count; shapeIndex++)
                {
                    shapes.Add(FromLegacy(body.Shapes[shapeIndex]));
                }

                bodies.Add(new NativeBody(
                    body.SourceId,
                    FromLegacy(body.Position),
                    FromLegacy(body.Orientation),
                    body.Friction,
                    body.Restitution,
                    shapes));
            }

            LegacySettings settings = artifact.WorldSettings;
            return new NativeArtifact(
                artifact.SchemaVersion,
                artifact.RuntimeCompatibilityId,
                artifact.LevelId,
                new NativeSettings(
                    FromLegacy(settings.Gravity),
                    settings.TickRate,
                    settings.SubstepCount,
                    settings.SolverIterations,
                    settings.RelaxationIterations,
                    settings.AllowDeactivation),
                bodies);
        }

        private static LegacyShape ToLegacy(NativeShape shape)
        {
            switch (shape.ShapeType)
            {
                case PhysicsShapeType.Box:
                    return LegacyShape.Box(
                        shape.ShapeKey, ToLegacy(shape.LocalPosition), ToLegacy(shape.LocalRotation),
                        ToLegacy(shape.Size));
                case PhysicsShapeType.Sphere:
                    return LegacyShape.Sphere(
                        shape.ShapeKey, ToLegacy(shape.LocalPosition), ToLegacy(shape.LocalRotation),
                        shape.Radius);
                case PhysicsShapeType.Capsule:
                    return LegacyShape.Capsule(
                        shape.ShapeKey, ToLegacy(shape.LocalPosition), ToLegacy(shape.LocalRotation),
                        shape.Radius, shape.Length);
                case PhysicsShapeType.Mesh:
                    var vertices = new LegacyVector[shape.Vertices.Length];
                    for (int index = 0; index < vertices.Length; index++)
                    {
                        vertices[index] = ToLegacy(shape.Vertices[index]);
                    }

                    return LegacyShape.Mesh(
                        shape.ShapeKey, ToLegacy(shape.LocalPosition), ToLegacy(shape.LocalRotation),
                        vertices, shape.Indices);
                default:
                    throw new ArgumentException("Unsupported native shape type.", nameof(shape));
            }
        }

        private static NativeShape FromLegacy(LegacyShape shape)
        {
            switch (shape.ShapeType)
            {
                case PhysicsShapeType.Box:
                    return NativeShape.Box(
                        shape.ShapeKey, FromLegacy(shape.LocalPosition), FromLegacy(shape.LocalRotation),
                        FromLegacy(shape.Size));
                case PhysicsShapeType.Sphere:
                    return NativeShape.Sphere(
                        shape.ShapeKey, FromLegacy(shape.LocalPosition), FromLegacy(shape.LocalRotation),
                        shape.Radius);
                case PhysicsShapeType.Capsule:
                    return NativeShape.Capsule(
                        shape.ShapeKey, FromLegacy(shape.LocalPosition), FromLegacy(shape.LocalRotation),
                        shape.Radius, shape.Length);
                case PhysicsShapeType.Mesh:
                    var vertices = new JVector[shape.Vertices.Length];
                    for (int index = 0; index < vertices.Length; index++)
                    {
                        vertices[index] = FromLegacy(shape.Vertices[index]);
                    }

                    return NativeShape.Mesh(
                        shape.ShapeKey, FromLegacy(shape.LocalPosition), FromLegacy(shape.LocalRotation),
                        vertices, shape.Indices);
                default:
                    throw new ArgumentException("Unsupported legacy shape type.", nameof(shape));
            }
        }

        private static LegacyVector ToLegacy(in JVector value) =>
            new LegacyVector(value.X, value.Y, value.Z);
        private static LegacyQuaternion ToLegacy(in JQuaternion value) =>
            new LegacyQuaternion(value.X, value.Y, value.Z, value.W);
        private static JVector FromLegacy(LegacyVector value) => new JVector(value.X, value.Y, value.Z);
        private static JQuaternion FromLegacy(LegacyQuaternion value) =>
            new JQuaternion(value.X, value.Y, value.Z, value.W);
    }
}
