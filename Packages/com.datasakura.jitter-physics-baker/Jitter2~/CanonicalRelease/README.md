# DataSakura canonical Jitter2.Core RC

This archive is the separately installable canonical Jitter runtime used by DataSakura Unity and
.NET consumers. It is not a UPM dependency and it does not install Jitter Physics Baker.

The release manifest pins the upstream commit, patched source-content hash, f32 compile profile,
public `Jitter2.LinearMath.StableMath` compatibility identity, and SHA-256 of every shipped file.
Accept an archive only when its Git tag, detached checksum, manifest, and clean-consumer verification
all agree.

The production profile is `f32`. A `USE_DOUBLE_PRECISION` build is incompatible and must be rejected
before simulation or deterministic content processing begins.

See `DIRECT_REFERENCE.md` for the Unity and .NET reference contract.
