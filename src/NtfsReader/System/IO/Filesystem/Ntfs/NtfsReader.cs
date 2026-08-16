/*
    The NtfsReader library.

    Copyright (C) 2008 Danny Couture

    This library is free software; you can redistribute it and/or
    modify it under the terms of the GNU Lesser General Public
    License as published by the Free Software Foundation; either
    version 2.1 of the License, or (at your option) any later version.

    This library is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
    Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public
    License along with this library; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
  
    For the full text of the license see the "License.txt" file.

    This library is based on the work of Jeroen Kessels, Author of JkDefrag.
    http://www.kessels.com/Jkdefrag/
    
    Special thanks goes to him.
  
    Danny Couture
    Software Architect
*/

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace System.IO.Filesystem.Ntfs
{
    public sealed partial class NtfsReader : IDisposable
    {
        #region Ntfs Structures

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private unsafe struct BootSector
        {
            fixed byte AlignmentOrReserved1[3];
            public readonly ulong Signature;
            public readonly ushort BytesPerSector;
            public readonly byte SectorsPerCluster;
            fixed byte AlignmentOrReserved2[26];
            public readonly ulong TotalSectors;
            public readonly ulong MftStartLcn;
            public readonly ulong Mft2StartLcn;
            public readonly uint ClustersPerMftRecord;
            public readonly uint ClustersPerIndexRecord;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct VolumeData
        {
            public readonly ulong VolumeSerialNumber;
            public readonly ulong NumberSectors;
            public readonly ulong TotalClusters;
            public readonly ulong FreeClusters;
            public readonly ulong TotalReserved;
            public readonly uint BytesPerSector;
            public readonly uint BytesPerCluster;
            public readonly uint BytesPerFileRecordSegment;
            public readonly uint ClustersPerFileRecordSegment;
            public readonly ulong MftValidDataLength;
            public readonly ulong MftStartLcn;
            public readonly ulong Mft2StartLcn;
            public readonly ulong MftZoneStart;
            public readonly ulong MftZoneEnd;
        }

        private enum RecordType : uint
        {
            File = 0x454c4946,  //'FILE' in ASCII
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct RecordHeader
        {
            public readonly RecordType Type;                  /* File type, for example 'FILE' */
            public readonly ushort UsaOffset;             /* Offset to the Update Sequence Array */
            public readonly ushort UsaCount;              /* Size in words of Update Sequence Array */
            public readonly ulong Lsn;                   /* $LogFile Sequence Number (LSN) */
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct INodeReference
        {
            public readonly uint InodeNumberLowPart;
            public readonly ushort InodeNumberHighPart;
            public readonly ushort SequenceNumber;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct FileRecordHeader
        {
            public readonly RecordHeader RecordHeader;
            public readonly ushort SequenceNumber;        /* Sequence number */
            public readonly ushort LinkCount;             /* Hard link count */
            public readonly ushort AttributeOffset;       /* Offset to the first Attribute */
            public readonly ushort Flags;                 /* Flags. bit 1 = in use, bit 2 = directory, bit 4 & 8 = unknown. */
            public readonly uint BytesInUse;             /* Real size of the FILE record */
            public readonly uint BytesAllocated;         /* Allocated size of the FILE record */
            public readonly INodeReference BaseFileRecord;     /* File reference to the base FILE record */
            public readonly ushort NextAttributeNumber;   /* Next Attribute Id */
            public readonly ushort Padding;               /* Align to 4 UCHAR boundary (XP) */
            public readonly uint MFTRecordNumber;        /* Number of this MFT Record (XP) */
            public readonly ushort UpdateSeqNum;          /*  */
        }

        private enum AttributeType : uint
        {
            AttributeInvalid = 0x00,         /* Not defined by Windows */
            AttributeStandardInformation = 0x10,
            AttributeAttributeList = 0x20,
            AttributeFileName = 0x30,
            AttributeObjectId = 0x40,
            AttributeSecurityDescriptor = 0x50,
            AttributeVolumeName = 0x60,
            AttributeVolumeInformation = 0x70,
            AttributeData = 0x80,
            AttributeIndexRoot = 0x90,
            AttributeIndexAllocation = 0xA0,
            AttributeBitmap = 0xB0,
            AttributeReparsePoint = 0xC0,         /* Reparse Point = Symbolic link */
            AttributeEAInformation = 0xD0,
            AttributeEA = 0xE0,
            AttributePropertySet = 0xF0,
            AttributeLoggedUtilityStream = 0x100
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Attribute
        {
            public readonly AttributeType AttributeType;
            public readonly uint Length;
            public readonly byte Nonresident;
            public readonly byte NameLength;
            public readonly ushort NameOffset;
            public readonly ushort Flags;              /* 0x0001 = Compressed, 0x4000 = Encrypted, 0x8000 = Sparse */
            public readonly ushort AttributeNumber;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private unsafe struct AttributeList
        {
            public readonly AttributeType AttributeType;
            public readonly ushort Length;
            public readonly byte NameLength;
            public readonly byte NameOffset;
            public readonly ulong LowestVcn;
            public readonly INodeReference FileReferenceNumber;
            public readonly ushort Instance;
            public fixed ushort AlignmentOrReserved[3];
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct AttributeFileName
        {
            public readonly INodeReference ParentDirectory;
            public readonly ulong CreationTime;
            public readonly ulong ChangeTime;
            public readonly ulong LastWriteTime;
            public readonly ulong LastAccessTime;
            public readonly ulong AllocatedSize;
            public readonly ulong DataSize;
            public readonly uint FileAttributes;
            public readonly uint AlignmentOrReserved;
            public readonly byte NameLength;
            public readonly byte NameType;                 /* NTFS=0x01, DOS=0x02 */
            public char Name;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct AttributeStandardInformation
        {
            public readonly ulong CreationTime;
            public readonly ulong FileChangeTime;
            public readonly ulong MftChangeTime;
            public readonly ulong LastAccessTime;
            public readonly uint FileAttributes;       /* READ_ONLY=0x01, HIDDEN=0x02, SYSTEM=0x04, VOLUME_ID=0x08, ARCHIVE=0x20, DEVICE=0x40 */
            public readonly uint MaximumVersions;
            public readonly uint VersionNumber;
            public readonly uint ClassId;
            public readonly uint OwnerId;                        // NTFS 3.0 only
            public readonly uint SecurityId;                     // NTFS 3.0 only
            public readonly ulong QuotaCharge;                // NTFS 3.0 only
            public readonly ulong Usn;                              // NTFS 3.0 only
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct ResidentAttribute
        {
            public readonly Attribute Attribute;
            public readonly uint ValueLength;
            public readonly ushort ValueOffset;
            public readonly ushort Flags;               // 0x0001 = Indexed
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private unsafe struct NonResidentAttribute
        {
            public readonly Attribute Attribute;
            public readonly ulong StartingVcn;
            public readonly ulong LastVcn;
            public readonly ushort RunArrayOffset;
            public readonly byte CompressionUnit;
            public fixed byte AlignmentOrReserved[5];
            public readonly ulong AllocatedSize;
            public readonly ulong DataSize;
            public readonly ulong InitializedSize;
            public readonly ulong CompressedSize;    // Only when compressed
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Fragment
        {
            public readonly ulong Lcn;                // Logical cluster number, location on disk.
            public readonly ulong NextVcn;            // Virtual cluster number of next fragment.

            public Fragment(ulong lcn, ulong nextVcn)
            {
                Lcn = lcn;
                NextVcn = nextVcn;
            }
        }

        #endregion

        #region Private Classes

        private sealed class Stream
        {
            public ulong Clusters;                      // Total number of clusters.
            public ulong Size;                          // Total number of bytes.
            public readonly AttributeType Type;
            public readonly int NameIndex;
            private List<Fragment> _fragments;

            public Stream(int nameIndex, AttributeType type, ulong size)
            {
                NameIndex = nameIndex;
                Type = type;
                Size = size;
            }

            public List<Fragment> Fragments => _fragments ?? (_fragments = new List<Fragment>(5));
        }

        /// <summary>
        /// Node struct for file and directory entries
        /// </summary>
        /// <remarks>
        /// We keep this as small as possible to reduce footprint for large volume.
        /// </remarks>
        private struct Node
        {
            public Attributes Attributes;
            public uint ParentNodeIndex;
            public ulong Size;
            public int NameIndex;
        }

        /// <summary>
        /// Contains extra information not required for basic purposes.
        /// </summary>
        private struct StandardInformation
        {
            public readonly ulong CreationTime;
            public readonly ulong LastAccessTime;
            public readonly ulong LastChangeTime;

            public StandardInformation(
                ulong creationTime,
                ulong lastAccessTime,
                ulong lastChangeTime
                )
            {
                CreationTime = creationTime;
                LastAccessTime = lastAccessTime;
                LastChangeTime = lastChangeTime;
            }
        }

        /// <summary>
        /// Add some functionality to the basic stream
        /// </summary>
        private sealed class FragmentWrapper : IFragment
        {
            readonly Fragment _fragment;

            public FragmentWrapper(StreamWrapper owner, Fragment fragment)
            {
                _fragment = fragment;
                _ = owner;
            }

            #region IFragment Members

            public ulong Lcn => _fragment.Lcn;

            public ulong NextVcn => _fragment.NextVcn;

            #endregion
        }

        /// <summary>
        /// Add some functionality to the basic stream
        /// </summary>
        private sealed class StreamWrapper : IStream
        {
            readonly NtfsReader _reader;
            readonly NodeWrapper _parentNode;
            readonly int _streamIndex;

            public StreamWrapper(NtfsReader reader, NodeWrapper parentNode, int streamIndex)
            {
                _reader = reader;
                _parentNode = parentNode;
                _streamIndex = streamIndex;
            }

            #region IStream Members

            /// <inheritdoc />
            public string Name => _reader.GetNameFromIndex(_reader._streams[_parentNode.NodeIndex][_streamIndex].NameIndex);

            /// <inheritdoc />
            public ulong Size => _reader._streams[_parentNode.NodeIndex][_streamIndex].Size;

            /// <inheritdoc />
            public IList<IFragment> Fragments
            {
                get 
                {
                    //if ((_reader._retrieveMode & RetrieveMode.Fragments) != RetrieveMode.Fragments)
                    //    throw new NotSupportedException("The fragments haven't been retrieved. Make sure to use the proper RetrieveMode.");

                    IList<Fragment> fragments =
                        _reader._streams[_parentNode.NodeIndex][_streamIndex].Fragments;

                    if (fragments == null || fragments.Count == 0)
                    {
                        return null;
                    }

                    return fragments.Select(fragment => new FragmentWrapper(this, fragment)).Cast<IFragment>().ToList();
                }
            }

            #endregion
        }

        /// <summary>
        /// Add some functionality to the basic node
        /// </summary>
        private sealed class NodeWrapper : INode
        {
            readonly NtfsReader _reader;
            readonly Node _node;
            string _fullName;

            public NodeWrapper(NtfsReader reader, uint nodeIndex, Node node)
            {
                _reader = reader;
                NodeIndex = nodeIndex;
                _node = node;
            }

            /// <inheritdoc />
            public uint NodeIndex { get; }

            /// <inheritdoc />
            public uint ParentNodeIndex => _node.ParentNodeIndex;

            /// <inheritdoc />
            public Attributes Attributes => _node.Attributes;

            /// <inheritdoc />
            public string Name => _reader.GetNameFromIndex(_node.NameIndex);

            /// <inheritdoc />
            public ulong Size => _node.Size;

            /// <inheritdoc />
            public string FullName => _fullName ?? (_fullName = _reader.GetNodeFullNameCore(NodeIndex));

            /// <inheritdoc />
            public IList<IStream> Streams
            {
                get 
                {
                    if (_reader._streams == null)
                    {
                        throw new NotSupportedException("The streams haven't been retrieved. Make sure to use the proper RetrieveMode.");
                    }

                    var streams = _reader._streams[NodeIndex];
                    if (streams == null)
                    {
                        return null;
                    }

                    var newStreams = new List<IStream>();
                    for (var i = 0; i < streams.Length; ++i)
                    {
                        newStreams.Add(new StreamWrapper(_reader, this, i));
                    }

                    return newStreams;
                }
            }

            #region INode Members

            public DateTime CreationTime
            {
                get 
                {
                    if (_reader._standardInformations == null)
                    {
                        throw new NotSupportedException("The StandardInformation haven't been retrieved. Make sure to use the proper RetrieveMode.");
                    }

                    return DateTime.FromFileTimeUtc((long)_reader._standardInformations[NodeIndex].CreationTime);
                }
            }

            public DateTime LastChangeTime
            {
                get 
                {
                    if (_reader._standardInformations == null)
                    {
                        throw new NotSupportedException("The StandardInformation haven't been retrieved. Make sure to use the proper RetrieveMode.");
                    }

                    return DateTime.FromFileTimeUtc((long)_reader._standardInformations[NodeIndex].LastChangeTime);
                }
            }

            public DateTime LastAccessTime
            {
                get 
                {
                    if (_reader._standardInformations == null)
                    {
                        throw new NotSupportedException("The StandardInformation haven't been retrieved. Make sure to use the proper RetrieveMode.");
                    }

                    return DateTime.FromFileTimeUtc((long)_reader._standardInformations[NodeIndex].LastAccessTime);
                }
            }

            #endregion
        }

        /// <summary>
        /// Simple structure of available disk informations.
        /// </summary>
        private sealed class DiskInfoWrapper : IDiskInfo
        {
            public ushort BytesPerSector;
            public byte SectorsPerCluster;
            public ulong TotalSectors;
            public ulong MftStartLcn;
            public ulong Mft2StartLcn;
            public uint ClustersPerMftRecord;
            public uint ClustersPerIndexRecord;
            public ulong BytesPerMftRecord;
            public ulong BytesPerCluster;
            public ulong TotalClusters;

            #region IDiskInfo Members

            /// <inheritdoc />
            ushort IDiskInfo.BytesPerSector => BytesPerSector;

            /// <inheritdoc />
            byte IDiskInfo.SectorsPerCluster => SectorsPerCluster;

            /// <inheritdoc />
            ulong IDiskInfo.TotalSectors => TotalSectors;

            /// <inheritdoc />
            ulong IDiskInfo.MftStartLcn => MftStartLcn;

            /// <inheritdoc />
            ulong IDiskInfo.Mft2StartLcn => Mft2StartLcn;

            /// <inheritdoc />
            uint IDiskInfo.ClustersPerMftRecord => ClustersPerMftRecord;

            /// <inheritdoc />
            uint IDiskInfo.ClustersPerIndexRecord => ClustersPerIndexRecord;

            /// <inheritdoc />
            ulong IDiskInfo.BytesPerMftRecord => BytesPerMftRecord;

            /// <inheritdoc />
            ulong IDiskInfo.BytesPerCluster => BytesPerCluster;

            /// <inheritdoc />
            ulong IDiskInfo.TotalClusters => TotalClusters;

            #endregion
        }

        #endregion

        #region Constants

        private const ulong VIRTUALFRAGMENT = 18446744073709551615; // _UI64_MAX - 1 */
        private const uint ROOTDIRECTORY = 5;

        private readonly byte[] _bitmapMasks = { 1, 2, 4, 8, 16, 32, 64, 128 };

        #endregion

        SafeFileHandle _volumeHandle;
        DiskInfoWrapper _diskInfo;
        readonly Node[] _nodes;
        StandardInformation[] _standardInformations;
        Stream[][] _streams;
        readonly DriveInfo _driveInfo;
        readonly List<string> _names = new List<string>();
        readonly RetrieveMode _retrieveMode;
        byte[] _bitmapData;

        //preallocate a lot of space for the strings to avoid too much dictionary resizing
        //use ordinal comparison to improve performance
        //this will be deallocated once the MFT reading is finished
        readonly Dictionary<string, int> _nameIndex = new Dictionary<string, int>(128 * 1024, StringComparer.Ordinal);

        #region Events

        /// <summary>
        /// Raised once the bitmap data has been read.
        /// </summary>
        public event EventHandler BitmapDataAvailable;

        private void OnBitmapDataAvailable() => BitmapDataAvailable?.Invoke(this, EventArgs.Empty);

        #endregion

        #region Helpers

        /// <summary>
        /// Allocate or retrieve an existing index for the particular string.
        /// </summary>
        ///<remarks>
        /// In order to minimize memory usage, we reuse string as much as possible.
        ///</remarks>
        private int GetNameIndex(string name)
        {
            if (_nameIndex.TryGetValue(name, out var existingIndex))
            {
                return existingIndex;
            }

            _names.Add(name);
            _nameIndex[name] = _names.Count - 1;

            return _names.Count - 1;
        }

        /// <summary>
        /// Get the string from our stringtable from the given index.
        /// </summary>
        private string GetNameFromIndex(int nameIndex) => nameIndex == 0 ? null : _names[nameIndex];

        private Stream SearchStream(List<Stream> streams, AttributeType streamType) =>
            //since the number of stream is usually small, we can afford O(n)
            streams.FirstOrDefault(stream => stream.Type == streamType);

        private Stream SearchStream(List<Stream> streams, AttributeType streamType, int streamNameIndex) =>
            //since the number of stream is usually small, we can afford O(n)
            streams.FirstOrDefault(stream => stream.Type == streamType && stream.NameIndex == streamNameIndex);

        #endregion

        #region File Reading Wrappers

        private unsafe void ReadFile(byte* buffer, int len, ulong absolutePosition) => ReadFile(buffer, (ulong)len, absolutePosition);

        private unsafe void ReadFile(byte* buffer, uint len, ulong absolutePosition) => ReadFile(buffer, (ulong)len, absolutePosition);

        private unsafe void ReadFile(byte* buffer, ulong len, ulong absolutePosition)
        {
            var overlapped = new NativeOverlapped(absolutePosition);

            if (!ReadFile(_volumeHandle, (IntPtr)buffer, (uint)len, out var read, ref overlapped))
            {
                throw new Exception("Unable to read volume information");
            }

            if (read != (uint)len)
            {
                throw new Exception("Unable to read volume information");
            }
        }

        #endregion

        #region Ntfs Interpretor

        /// <summary>
        /// Read the next contiguous block of information on disk
        /// </summary>
        private unsafe bool ReadNextChunk(
            byte* buffer,
            uint bufferSize,
            uint nodeIndex,
            int fragmentIndex,
            Stream dataStream,
            ref ulong BlockStart,
            ref ulong BlockEnd,
            ref ulong Vcn,
            ref ulong RealVcn
            )
        {
            BlockStart = nodeIndex;
            BlockEnd = BlockStart + bufferSize / _diskInfo.BytesPerMftRecord;
            if (BlockEnd > dataStream.Size * 8)
            {
                BlockEnd = dataStream.Size * 8;
            }

            ulong u1 = 0;

            var fragmentCount = dataStream.Fragments.Count;
            while (fragmentIndex < fragmentCount)
            {
                var fragment = dataStream.Fragments[fragmentIndex];

                /* Calculate Inode at the end of the fragment. */
                u1 = (RealVcn + fragment.NextVcn - Vcn) * _diskInfo.BytesPerSector * _diskInfo.SectorsPerCluster / _diskInfo.BytesPerMftRecord;

                if (u1 > nodeIndex)
                {
                    break;
                }

                do
                {
                    if (fragment.Lcn != VIRTUALFRAGMENT)
                    {
                        RealVcn = RealVcn + fragment.NextVcn - Vcn;
                    }

                    Vcn = fragment.NextVcn;

                    if (++fragmentIndex >= fragmentCount)
                    {
                        break;
                    }
                } while (fragment.Lcn == VIRTUALFRAGMENT);
            }

            if (fragmentIndex >= fragmentCount)
            {
                return false;
            }

            if (BlockEnd >= u1)
            {
                BlockEnd = u1;
            }

            var position =
                (dataStream.Fragments[fragmentIndex].Lcn - RealVcn) * _diskInfo.BytesPerSector *
                    _diskInfo.SectorsPerCluster + BlockStart * _diskInfo.BytesPerMftRecord;

            ReadFile(buffer, (BlockEnd - BlockStart) * _diskInfo.BytesPerMftRecord, position);

            return true;
        }

        /// <summary>
        /// Gather basic disk information we need to interpret data
        /// </summary>
        private unsafe void InitializeDiskInfo()
        {
            var volumeData = new byte[512];

            fixed (byte* ptr = volumeData)
            {
                ReadFile(ptr, volumeData.Length, 0);

                var bootSector = (BootSector*)ptr;

                if (bootSector->Signature != 0x202020205346544E)
                {
                    throw new Exception("This is not an NTFS disk.");
                }

                var diskInfo = new DiskInfoWrapper
                {
                    BytesPerSector = bootSector->BytesPerSector,
                    SectorsPerCluster = bootSector->SectorsPerCluster,
                    TotalSectors = bootSector->TotalSectors,
                    MftStartLcn = bootSector->MftStartLcn,
                    Mft2StartLcn = bootSector->Mft2StartLcn,
                    ClustersPerMftRecord = bootSector->ClustersPerMftRecord,
                    ClustersPerIndexRecord = bootSector->ClustersPerIndexRecord
                };

                if (bootSector->ClustersPerMftRecord >= 128)
                {
                    diskInfo.BytesPerMftRecord = ((ulong)1 << (byte)(256 - (byte)bootSector->ClustersPerMftRecord));
                }
                else
                {
                    diskInfo.BytesPerMftRecord = diskInfo.ClustersPerMftRecord * diskInfo.BytesPerSector * diskInfo.SectorsPerCluster;
                }

                diskInfo.BytesPerCluster = diskInfo.BytesPerSector * (ulong)diskInfo.SectorsPerCluster;

                if (diskInfo.SectorsPerCluster > 0)
                {
                    diskInfo.TotalClusters = diskInfo.TotalSectors / diskInfo.SectorsPerCluster;
                }

                _diskInfo = diskInfo;
            }
        }

        /// <summary>
        /// Used to check/adjust data before we begin to interpret it
        /// </summary>
        private unsafe void FixupRawMftdata(byte* buffer, ulong len)
        {
            var ntfsFileRecordHeader = (FileRecordHeader*)buffer;

            if (ntfsFileRecordHeader->RecordHeader.Type != RecordType.File)
            {
                return;
            }

            var wordBuffer = (ushort*)buffer;

            var UpdateSequenceArray = (ushort*)(buffer + ntfsFileRecordHeader->RecordHeader.UsaOffset);
            var increment = (uint)_diskInfo.BytesPerSector / sizeof(ushort);

            var Index = increment - 1;

            for (var i = 1; i < ntfsFileRecordHeader->RecordHeader.UsaCount; i++)
            {
                /* Check if we are inside the buffer. */
                if (Index * sizeof(ushort) >= len)
                {
                    throw new Exception("USA data indicates that data is missing, the MFT may be corrupt.");
                }

                // Check if the last 2 bytes of the sector contain the Update Sequence Number.
                if (wordBuffer[Index] != UpdateSequenceArray[0])
                {
                    throw new Exception("USA fixup word is not equal to the Update Sequence Number, the MFT may be corrupt.");
                }

                /* Replace the last 2 bytes in the sector with the value from the Usa array. */
                wordBuffer[Index] = UpdateSequenceArray[i];
                Index += increment;
            }
        }

        /// <summary>
        /// Decode the RunLength value.
        /// </summary>
        private static unsafe long ProcessRunLength(byte* runData, uint runDataLength, int runLengthSize, ref uint index)
        {
            long runLength = 0;
            var runLengthBytes = (byte*)&runLength;
            for (var i = 0; i < runLengthSize; i++)
            {
                runLengthBytes[i] = runData[index];
                if (++index >= runDataLength)
                {
                    throw new Exception("Datarun is longer than buffer, the MFT may be corrupt.");
                }
            }
            return runLength;
        }

        /// <summary>
        /// Decode the RunOffset value.
        /// </summary>
        private static unsafe long ProcessRunOffset(byte* runData, uint runDataLength, int runOffsetSize, ref uint index)
        {
            long runOffset = 0;
            var runOffsetBytes = (byte*)&runOffset;

            int i;
            for (i = 0; i < runOffsetSize; i++)
            {
                runOffsetBytes[i] = runData[index];
                if (++index >= runDataLength)
                {
                    throw new Exception("Datarun is longer than buffer, the MFT may be corrupt.");
                }
            }

            //process negative values
            if (runOffsetBytes[i - 1] >= 0x80)
            {
                while (i < 8)
                    runOffsetBytes[i++] = 0xFF;
            }

            return runOffset;
        }

        /// <summary>
        /// Read the data that is specified in a RunData list from disk into memory,
        /// skipping the first Offset bytes.
        /// </summary>
        private unsafe byte[] ProcessNonResidentData(
            byte* RunData,
            uint RunDataLength,
            ulong Offset,         /* Bytes to skip from begin of data. */
            ulong WantedLength    /* Number of bytes to read. */
            )
        {
            /* Sanity check. */
            if (RunData == null || RunDataLength == 0)
            {
                throw new Exception("nothing to read");
            }

            if (WantedLength >= uint.MaxValue)
            {
                throw new Exception("too many bytes to read");
            }

            /* We have to round up the WantedLength to the nearest sector. For some
               reason or other Microsoft has decided that raw reading from disk can
               only be done by whole sector, even though ReadFile() accepts it's
               parameters in bytes. */
            if (WantedLength % _diskInfo.BytesPerSector > 0)
            {
                WantedLength += _diskInfo.BytesPerSector - (WantedLength % _diskInfo.BytesPerSector);
            }

            /* Walk through the RunData and read the requested data from disk. */
            uint Index = 0;
            long Lcn = 0;
            long Vcn = 0;

            var buffer = new byte[WantedLength];

            fixed (byte* bufPtr = buffer)
            {
                while (RunData[Index] != 0)
                {
                    /* Decode the RunData and calculate the next Lcn. */
                    var RunLengthSize = (RunData[Index] & 0x0F);
                    var RunOffsetSize = ((RunData[Index] & 0xF0) >> 4);
                    
                    if (++Index >= RunDataLength)
                    {
                        throw new Exception("Error: datarun is longer than buffer, the MFT may be corrupt.");
                    }

                    var RunLength =
                        ProcessRunLength(RunData, RunDataLength, RunLengthSize, ref Index);

                    var RunOffset =
                        ProcessRunOffset(RunData, RunDataLength, RunOffsetSize, ref Index);

                    // Ignore virtual extents.
                    if (RunOffset == 0 || RunLength == 0)
                    {
                        continue;
                    }

                    Lcn += RunOffset;
                    Vcn += RunLength;

                    /* Determine how many and which bytes we want to read. If we don't need
                       any bytes from this extent then loop. */
                    var ExtentVcn = (ulong)((Vcn - RunLength) * _diskInfo.BytesPerSector * _diskInfo.SectorsPerCluster);
                    var ExtentLcn = (ulong)(Lcn * _diskInfo.BytesPerSector * _diskInfo.SectorsPerCluster);
                    var ExtentLength = (ulong)(RunLength * _diskInfo.BytesPerSector * _diskInfo.SectorsPerCluster);

                    if (Offset >= ExtentVcn + ExtentLength)
                    {
                        continue;
                    }

                    if (Offset > ExtentVcn)
                    {
                        ExtentLcn = ExtentLcn + Offset - ExtentVcn;
                        ExtentLength -= (Offset - ExtentVcn);
                        ExtentVcn = Offset;
                    }

                    if (Offset + WantedLength <= ExtentVcn)
                    {
                        continue;
                    }

                    if (Offset + WantedLength < ExtentVcn + ExtentLength)
                    {
                        ExtentLength = Offset + WantedLength - ExtentVcn;
                    }

                    if (ExtentLength == 0)
                    {
                        continue;
                    }

                    ReadFile(bufPtr + ExtentVcn - Offset, ExtentLength, ExtentLcn);
                }
            }

            return buffer;
        }

        /// <summary>
        /// Process each attributes and gather information when necessary
        /// </summary>
        private unsafe void ProcessAttributes(ref Node node, uint nodeIndex, byte* ptr, ulong bufLength, ushort instance, int depth, List<Stream> streams, bool isMftNode)
        {
            Attribute* attribute;
            for (uint attributeOffset = 0; attributeOffset < bufLength; attributeOffset += attribute->Length)
            {
                attribute = (Attribute*)(ptr + attributeOffset);

                // exit the loop if end-marker.
                if ((attributeOffset + 4 <= bufLength) &&
                    (*(uint*)attribute == 0xFFFFFFFF))
                {
                    break;
                }

                //make sure we did read the data correctly
                if ((attributeOffset + 4 > bufLength) || attribute->Length < 3 ||
                    (attributeOffset + attribute->Length > bufLength))
                {
                    throw new Exception("Error: attribute in Inode %I64u is bigger than the data, the MFT may be corrupt.");
                }

                //attributes list needs to be processed at the end
                if (attribute->AttributeType == AttributeType.AttributeAttributeList)
                {
                    continue;
                }

                /* If the Instance does not equal the AttributeNumber then ignore the attribute.
                   This is used when an AttributeList is being processed and we only want a specific
                   instance. */
                if ((instance != 65535) && (instance != attribute->AttributeNumber))
                {
                    continue;
                }

                if (attribute->Nonresident == 0)
                {
                    var residentAttribute = (ResidentAttribute*)attribute;

                    switch (attribute->AttributeType)
                    {
                        case AttributeType.AttributeFileName:
                            var attributeFileName = (AttributeFileName*)(ptr + attributeOffset + residentAttribute->ValueOffset);

                            if (attributeFileName->ParentDirectory.InodeNumberHighPart > 0)
                            {
                                throw new NotSupportedException("48 bits inode are not supported to reduce memory footprint.");
                            }

                            //node.ParentNodeIndex = ((UInt64)attributeFileName->ParentDirectory.InodeNumberHighPart << 32) + attributeFileName->ParentDirectory.InodeNumberLowPart;
                            node.ParentNodeIndex = attributeFileName->ParentDirectory.InodeNumberLowPart;

                            if (attributeFileName->NameType == 1 || node.NameIndex == 0)
                            {
                                node.NameIndex = GetNameIndex(new string(&attributeFileName->Name, 0, attributeFileName->NameLength));
                            }

                            break;

                        case AttributeType.AttributeStandardInformation:
                            var attributeStandardInformation = (AttributeStandardInformation*)(ptr + attributeOffset + residentAttribute->ValueOffset);

                            node.Attributes |= (Attributes)attributeStandardInformation->FileAttributes;

                            if ((_retrieveMode & RetrieveMode.StandardInformations) == RetrieveMode.StandardInformations)
                            {
                                _standardInformations[nodeIndex] =
                                    new StandardInformation(
                                        attributeStandardInformation->CreationTime,
                                        attributeStandardInformation->FileChangeTime,
                                        attributeStandardInformation->LastAccessTime
                                    );
                            }

                            break;

                        case AttributeType.AttributeData:
                            node.Size = residentAttribute->ValueLength;
                            break;
                    }
                }
                else
                {
                    var nonResidentAttribute = (NonResidentAttribute*)attribute;

                    //save the length (number of bytes) of the data.
                    if (attribute->AttributeType == AttributeType.AttributeData && node.Size == 0)
                    {
                        node.Size = nonResidentAttribute->DataSize;
                    }

                    if (streams == null)
                    {
                        continue;
                    }

                    //extract the stream name
                    var streamNameIndex = 0;
                    if (attribute->NameLength > 0)
                    {
                        streamNameIndex = GetNameIndex(new string((char*)(ptr + attributeOffset + attribute->NameOffset), 0, attribute->NameLength));
                    }

                    //find or create the stream
                    var stream = 
                        SearchStream(streams, attribute->AttributeType, streamNameIndex);

                    if (stream == null)
                    {
                        stream = new Stream(streamNameIndex, attribute->AttributeType, nonResidentAttribute->DataSize);
                        streams.Add(stream);
                    }
                    else if (stream.Size == 0)
                    {
                        stream.Size = nonResidentAttribute->DataSize;
                    }

                    //we need the fragment of the MFTNode so retrieve them this time
                    //even if fragments aren't normally read
                    if (isMftNode || (_retrieveMode & RetrieveMode.Fragments) == RetrieveMode.Fragments)
                    {
                        ProcessFragments(
                            ref node,
                            stream,
                            ptr + attributeOffset + nonResidentAttribute->RunArrayOffset,
                            attribute->Length - nonResidentAttribute->RunArrayOffset,
                            nonResidentAttribute->StartingVcn
                        );
                    }
                }
            }

            //for (uint AttributeOffset = 0; AttributeOffset < BufLength; AttributeOffset = AttributeOffset + attribute->Length)
            //{
            //    attribute = (Attribute*)&ptr[AttributeOffset];

            //    if (*(UInt32*)attribute == 0xFFFFFFFF)
            //        break;

            //    if (attribute->AttributeType != AttributeType.AttributeAttributeList)
            //        continue;

            //    if (attribute->Nonresident == 0)
            //    {
            //        ResidentAttribute* residentAttribute = (ResidentAttribute*)attribute;

            //        ProcessAttributeList(
            //            node,
            //            ptr + AttributeOffset + residentAttribute->ValueOffset,
            //            residentAttribute->ValueLength,
            //            depth
            //            );
            //    }
            //    else
            //    {
            //        NonResidentAttribute* nonResidentAttribute = (NonResidentAttribute*)attribute;

            //        byte[] buffer =
            //            ProcessNonResidentData(
            //                ptr + AttributeOffset + nonResidentAttribute->RunArrayOffset,
            //                attribute->Length - nonResidentAttribute->RunArrayOffset,
            //                0,
            //                nonResidentAttribute->DataSize
            //          );

            //        fixed (byte* bufPtr = buffer)
            //            ProcessAttributeList(node, bufPtr, nonResidentAttribute->DataSize, depth + 1);
            //    }
            //}

            if (streams != null && streams.Count > 0)
            {
                node.Size = streams[0].Size;
            }
        }

        //private unsafe void ProcessAttributeList(Node mftNode, Node node, byte* ptr, UInt64 bufLength, int depth, InterpretMode interpretMode)
        //{
        //    if (ptr == null || bufLength == 0)
        //        return;

        //    if (depth > 1000)
        //        throw new Exception("Error: infinite attribute loop, the MFT may be corrupt.");

        //    AttributeList* attribute = null;
        //    for (uint AttributeOffset = 0; AttributeOffset < bufLength; AttributeOffset = AttributeOffset + attribute->Length)
        //    {
        //        attribute = (AttributeList*)&ptr[AttributeOffset];

        //        /* Exit if no more attributes. AttributeLists are usually not closed by the
        //           0xFFFFFFFF endmarker. Reaching the end of the buffer is therefore normal and
        //           not an error. */
        //        if (AttributeOffset + 3 > bufLength) break;
        //        if (*(UInt32*)attribute == 0xFFFFFFFF) break;
        //        if (attribute->Length < 3) break;
        //        if (AttributeOffset + attribute->Length > bufLength) break;

        //        /* Extract the referenced Inode. If it's the same as the calling Inode then ignore
        //           (if we don't ignore then the program will loop forever, because for some
        //           reason the info in the calling Inode is duplicated here...). */
        //        UInt64 RefInode = ((UInt64)attribute->FileReferenceNumber.InodeNumberHighPart << 32) + attribute->FileReferenceNumber.InodeNumberLowPart;
        //        if (RefInode == node.NodeIndex)
        //            continue;

        //        /* Extract the streamname. I don't know why AttributeLists can have names, and
        //           the name is not used further down. It is only extracted for debugging purposes.
        //           */
        //        string streamName;
        //        if (attribute->NameLength > 0)
        //            streamName = new string((char*)((UInt64)ptr + AttributeOffset + attribute->NameOffset), 0, attribute->NameLength);

        //        /* Find the fragment in the MFT that contains the referenced Inode. */
        //        UInt64 Vcn = 0;
        //        UInt64 RealVcn = 0;
        //        UInt64 RefInodeVcn = (RefInode * _diskInfo.BytesPerMftRecord) / (UInt64)(_diskInfo.BytesPerSector * _diskInfo.SectorsPerCluster);

        //        Stream dataStream = null;
        //        foreach (Stream stream in mftNode.Streams)
        //            if (stream.Type == AttributeType.AttributeData)
        //            {
        //                dataStream = stream;
        //                break;
        //            }

        //        Fragment? fragment = null;
        //        for (int i = 0; i < dataStream.Fragments.Count; ++i)
        //        {
        //            fragment = dataStream.Fragments[i];

        //            if (fragment.Value.Lcn != VIRTUALFRAGMENT)
        //            {
        //                if ((RefInodeVcn >= RealVcn) && (RefInodeVcn < RealVcn + fragment.Value.NextVcn - Vcn))
        //                    break;

        //                RealVcn = RealVcn + fragment.Value.NextVcn - Vcn;
        //            }

        //            Vcn = fragment.Value.NextVcn;
        //        }

        //        if (fragment == null)
        //            throw new Exception("Error: Inode %I64u is an extension of Inode %I64u, but does not exist (outside the MFT).");

        //        /* Fetch the record of the referenced Inode from disk. */
        //        byte[] buffer = new byte[_diskInfo.BytesPerMftRecord];

        //        NativeOverlapped overlapped =
        //            new NativeOverlapped(
        //                fragment.Value.Lcn - RealVcn * _diskInfo.BytesPerSector * _diskInfo.SectorsPerCluster + RefInode * _diskInfo.BytesPerMftRecord
        //                );

        //        fixed (byte* bufPtr = buffer)
        //        {
        //            uint read;
        //            bool result =
        //                ReadFile(
        //                    _volumeHandle,
        //                    (IntPtr)bufPtr,
        //                    (UInt32)_diskInfo.BytesPerMftRecord,
        //                    out read,
        //                    ref overlapped
        //                    );

        //            if (!result)
        //                throw new Exception("error reading disk");

        //            /* Fixup the raw data. */
        //            FixupRawMftdata(bufPtr, _diskInfo.BytesPerMftRecord);

        //            /* If the Inode is not in use then skip. */
        //            FileRecordHeader* fileRecordHeader = (FileRecordHeader*)bufPtr;
        //            if ((fileRecordHeader->Flags & 1) != 1)
        //                continue;

        //            ///* If the BaseInode inside the Inode is not the same as the calling Inode then
        //            //   skip. */
        //            UInt64 baseInode = ((UInt64)fileRecordHeader->BaseFileRecord.InodeNumberHighPart << 32) + fileRecordHeader->BaseFileRecord.InodeNumberLowPart;
        //            if (node.NodeIndex != baseInode)
        //                continue;

        //            ///* Process the list of attributes in the Inode, by recursively calling the
        //            //   ProcessAttributes() subroutine. */
        //            ProcessAttributes(
        //                node,
        //                bufPtr + fileRecordHeader->AttributeOffset,
        //                _diskInfo.BytesPerMftRecord - fileRecordHeader->AttributeOffset,
        //                attribute->Instance,
        //                depth + 1
        //                );
        //        }
        //    }
        //}

        /// <summary>
        /// Process fragments for streams
        /// </summary>
        private unsafe void ProcessFragments(
            ref Node node,
            Stream stream,
            byte* runData,
            uint runDataLength,
            ulong startingVcn)
        {
            if (runData == null)
            {
                return;
            }

            /* Walk through the RunData and add the extents. */
            uint index = 0;
            long lcn = 0;
            var vcn = (long)startingVcn;

            while (runData[index] != 0)
            {
                /* Decode the RunData and calculate the next Lcn. */
                var runLengthSize = (runData[index] & 0x0F);
                var runOffsetSize = ((runData[index] & 0xF0) >> 4);

                if (++index >= runDataLength)
                {
                    throw new Exception("Error: datarun is longer than buffer, the MFT may be corrupt.");
                }

                var runLength = 
                    ProcessRunLength(runData, runDataLength, runLengthSize, ref index);

                var runOffset =
                    ProcessRunOffset(runData, runDataLength, runOffsetSize, ref index);
             
                lcn += runOffset;
                vcn += runLength;

                /* Add the size of the fragment to the total number of clusters.
                   There are two kinds of fragments: real and virtual. The latter do not
                   occupy clusters on disk, but are information used by compressed
                   and sparse files. */
                if (runOffset != 0)
                {
                    stream.Clusters += (ulong)runLength;
                }

                stream.Fragments.Add(
                    new Fragment(
                        runOffset == 0 ? VIRTUALFRAGMENT : (ulong)lcn,
                        (ulong)vcn
                    )
                );
            }
        }
        
        /// <summary>
        /// Process an actual MFT record from the buffer
        /// </summary>
        private unsafe bool ProcessMftRecord(byte* buffer, ulong length, uint nodeIndex, out Node node, List<Stream> streams, bool isMftNode)
        {
            node = new Node();

            var ntfsFileRecordHeader = (FileRecordHeader*)buffer;

            if (ntfsFileRecordHeader->RecordHeader.Type != RecordType.File)
            {
                return false;
            }

            //the inode is not in use
            if ((ntfsFileRecordHeader->Flags & 1) != 1)
            {
                return false;
            }

            var baseInode = ((ulong)ntfsFileRecordHeader->BaseFileRecord.InodeNumberHighPart << 32) + ntfsFileRecordHeader->BaseFileRecord.InodeNumberLowPart;

            //This is an inode extension used in an AttributeAttributeList of another inode, don't parse it
            if (baseInode != 0)
            {
                return false;
            }

            if (ntfsFileRecordHeader->AttributeOffset >= length)
            {
                throw new Exception("Error: attributes in Inode %I64u are outside the FILE record, the MFT may be corrupt.");
            }

            if (ntfsFileRecordHeader->BytesInUse > length)
            {
                throw new Exception("Error: in Inode %I64u the record is bigger than the size of the buffer, the MFT may be corrupt.");
            }

            //make the file appear in the rootdirectory by default
            node.ParentNodeIndex = ROOTDIRECTORY;
            
            if ((ntfsFileRecordHeader->Flags & 2) == 2)
            {
                node.Attributes |= Attributes.Directory;
            }

            ProcessAttributes(ref node, nodeIndex, buffer + ntfsFileRecordHeader->AttributeOffset, length - ntfsFileRecordHeader->AttributeOffset, 65535, 0, streams, isMftNode);

            return true;
        }

        /// <summary>
        /// Process the bitmap data that contains information on inode usage.
        /// </summary>
        private unsafe byte[] ProcessBitmapData(List<Stream> streams)
        {
            ulong vcn = 0;
            ulong maxMftBitmapBytes = 0;

            var bitmapStream = SearchStream(streams, AttributeType.AttributeBitmap);
            if (bitmapStream == null)
            {
                throw new Exception("No Bitmap Data");
            }

            foreach (var fragment in bitmapStream.Fragments)
            {
                if (fragment.Lcn != VIRTUALFRAGMENT)
                {
                    maxMftBitmapBytes += (fragment.NextVcn - vcn) * _diskInfo.BytesPerSector * _diskInfo.SectorsPerCluster;
                }

                vcn = fragment.NextVcn;
            }

            var bitmapData = new byte[maxMftBitmapBytes];

            fixed (byte* bitmapDataPtr = bitmapData)
            {
                vcn = 0;
                ulong realVcn = 0;

                foreach (var fragment in bitmapStream.Fragments)
                {
                    if (fragment.Lcn != VIRTUALFRAGMENT)
                    {
                        ReadFile(
                            bitmapDataPtr + realVcn * _diskInfo.BytesPerSector * _diskInfo.SectorsPerCluster,
                            (fragment.NextVcn - vcn) * _diskInfo.BytesPerSector * _diskInfo.SectorsPerCluster,
                            fragment.Lcn * _diskInfo.BytesPerSector * _diskInfo.SectorsPerCluster
                            );

                        realVcn = realVcn + fragment.NextVcn - vcn;
                    }

                    vcn = fragment.NextVcn;
                }
            }

            return bitmapData;
        }

        /// <summary>
        /// Begin the process of interpreting MFT data
        /// </summary>
        private unsafe Node[] ProcessMft()
        {
            //64 KB seems to be optimal for Windows XP, Vista is happier with 256KB...
            var bufferSize =
                (Environment.OSVersion.Version.Major >= 6 ? 256u : 64u) * 1024;

            var data = new byte[bufferSize];

            fixed (byte* buffer = data)
            {
                //Read the $MFT record from disk into memory, which is always the first record in the MFT. 
                ReadFile(buffer, _diskInfo.BytesPerMftRecord, _diskInfo.MftStartLcn * _diskInfo.BytesPerSector * _diskInfo.SectorsPerCluster);

                //Fixup the raw data from disk. This will also test if it's a valid $MFT record.
                FixupRawMftdata(buffer, _diskInfo.BytesPerMftRecord);

                var mftStreams = new List<Stream>();

                if ((_retrieveMode & RetrieveMode.StandardInformations) == RetrieveMode.StandardInformations)
                {
                    _standardInformations = new StandardInformation[1]; //allocate some space for $MFT record
                }

                if (!ProcessMftRecord(buffer, _diskInfo.BytesPerMftRecord, 0, out var mftNode, mftStreams, true))
                {
                    throw new Exception("Can't interpret Mft Record");
                }

                //the bitmap data contains all used inodes on the disk
                _bitmapData =
                    ProcessBitmapData(mftStreams);

                OnBitmapDataAvailable();

                var dataStream = SearchStream(mftStreams, AttributeType.AttributeData);

                var maxInode = (uint)_bitmapData.Length * 8;
                if (maxInode > (uint)(dataStream.Size / _diskInfo.BytesPerMftRecord))
                {
                    maxInode = (uint)(dataStream.Size / _diskInfo.BytesPerMftRecord);
                }

                var nodes = new Node[maxInode];
                nodes[0] = mftNode;

                if ((_retrieveMode & RetrieveMode.StandardInformations) == RetrieveMode.StandardInformations)
                {
                    var mftRecordInformation = _standardInformations[0];
                    _standardInformations = new StandardInformation[maxInode];
                    _standardInformations[0] = mftRecordInformation;
                }

                if ((_retrieveMode & RetrieveMode.Streams) == RetrieveMode.Streams)
                {
                    _streams = new Stream[maxInode][];
                }

                /* Read and process all the records in the MFT. The records are read into a
                   buffer and then given one by one to the InterpretMftRecord() subroutine. */

                ulong blockStart = 0, blockEnd = 0;
                ulong realVcn = 0, vcn = 0;

                var stopwatch = new Stopwatch();
                stopwatch.Start();

                ulong totalBytesRead = 0;
                const int fragmentIndex = 0;
                var fragmentCount = dataStream.Fragments.Count;
                for (uint nodeIndex = 1; nodeIndex < maxInode; nodeIndex++)
                {
                    // Ignore the Inode if the bitmap says it's not in use.
                    if ((_bitmapData[nodeIndex >> 3] & _bitmapMasks[nodeIndex % 8]) == 0)
                    {
                        continue;
                    }

                    if (nodeIndex >= blockEnd)
                    {
                        if (!ReadNextChunk(
                                buffer,
                                bufferSize, 
                                nodeIndex, 
                                fragmentIndex,
                                dataStream, 
                                ref blockStart, 
                                ref blockEnd, 
                                ref vcn, 
                                ref realVcn))
                        {
                            break;
                        }

                        totalBytesRead += (blockEnd - blockStart) * _diskInfo.BytesPerMftRecord;
                    }

                    FixupRawMftdata(
                            buffer + (nodeIndex - blockStart) * _diskInfo.BytesPerMftRecord,
                            _diskInfo.BytesPerMftRecord
                        );

                    List<Stream> streams = null;
                    if ((_retrieveMode & RetrieveMode.Streams) == RetrieveMode.Streams)
                    {
                        streams = new List<Stream>();
                    }

                    if (!ProcessMftRecord(
                            buffer + (nodeIndex - blockStart) * _diskInfo.BytesPerMftRecord,
                            _diskInfo.BytesPerMftRecord,
                            nodeIndex,
                            out var newNode,
                            streams,
                            false))
                    {
                        continue;
                    }

                    nodes[nodeIndex] = newNode;

                    if (streams != null)
                    {
                        _streams[nodeIndex] = streams.ToArray();
                    }
                }

                stopwatch.Stop();

                Trace.WriteLine(
                    $"{(float) totalBytesRead / (1024 * 1024):F3} MB of volume metadata has been read in {(float) stopwatch.Elapsed.TotalSeconds:F3} s at {((float) totalBytesRead / (1024 * 1024)) / stopwatch.Elapsed.TotalSeconds:F3} MB/s"
                );

                return nodes;
            }
        }

        #endregion
    }
}
