using System;
using System.IO;

namespace DOL.GS
{
    /// <summary>
    /// Structural check of a Detour MSET navmesh file before the native loader (DetourInterface.cpp LoadNavMesh) reads it.
    /// That loader ignores short reads and addTile failures, so a truncated or damaged file becomes tiles of zeroed data
    /// that crash Detour (0xC0000005) only when a path search reaches them (DR-376: one sandbox died 26 s after boot in
    /// PathStraight). Layout: header {int magic 'MSET', int version 1, int numTiles, dtNavMeshParams 28 bytes} = 40 bytes;
    /// then per tile {uint64 tileRef, int dataSize, 4 bytes padding} = 16 bytes and dataSize bytes of tile data, which
    /// start with Detour's dtMeshHeader {int magic 'DNAV', int version}.
    /// </summary>
    public static class NavMeshFileCheck
    {
        private const int SetMagic = 0x4d534554;   // 'MSET'
        private const int SetVersion = 1;
        private const int TileMagic = 0x444E4156;  // 'DNAV'
        private const int TileVersion = 7;
        private const int SetHeaderSize = 40;
        private const int TileHeaderSize = 16;

        /// <summary>Null when the file is whole; otherwise what is wrong with it.</summary>
        public static string Problem(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                return Problem(stream);
            }
            catch (Exception exception)
            {
                return $"unreadable: {exception.Message}";
            }
        }

        public static string Problem(Stream stream)
        {
            long length = stream.Length;
            using var reader = new BinaryReader(stream);
            if (length < SetHeaderSize) return $"only {length} bytes, shorter than the header";
            int magic = reader.ReadInt32(), version = reader.ReadInt32(), tiles = reader.ReadInt32();
            if (magic != SetMagic) return $"not a Detour MSET file (magic 0x{magic:x8})";
            if (version != SetVersion) return $"MSET version {version}, expected {SetVersion}";
            if (tiles < 0) return $"negative tile count {tiles}";
            stream.Position = SetHeaderSize;
            for (int tile = 0; tile < tiles; tile++)
            {
                if (length - stream.Position < TileHeaderSize)
                    return $"ends inside tile header {tile + 1} of {tiles}";
                ulong tileRef = reader.ReadUInt64();
                int size = reader.ReadInt32();
                reader.ReadInt32(); // padding
                // The native loader stops at the first empty tile header, as it always has.
                if (tileRef == 0 || size == 0) return null;
                if (size < 8) return $"tile {tile + 1} claims {size} bytes";
                if (length - stream.Position < size)
                    return $"tile {tile + 1} of {tiles} claims {size} bytes but only {length - stream.Position} remain";
                long dataStart = stream.Position;
                int tileMagic = reader.ReadInt32(), tileVersion = reader.ReadInt32();
                if (tileMagic != TileMagic || tileVersion != TileVersion)
                    return $"tile {tile + 1} data is not a Detour v{TileVersion} tile (magic 0x{tileMagic:x8}, version {tileVersion})";
                stream.Position = dataStart + size;
            }
            return null;
        }
    }
}
