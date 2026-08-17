// Before the split, all of this code was one assembly and `internal` meant "engine-internal".
// Splitting it into Common/client/server turned every one of those into a compile error at the
// new boundary. Rather than widen dozens of members to public - which would advertise them as
// API when they aren't - the two hosts get explicit access to Common's internals.
//
// This is not a hole in the Stage 0 boundary. That boundary is about *dependency direction*:
// Common must not reference Silk.NET/ImGui/SFML, and it still doesn't. Letting the hosts see
// Common's internals doesn't let Common see theirs.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("VoxelEngine")]
[assembly: InternalsVisibleTo("voxelengine_server")]
