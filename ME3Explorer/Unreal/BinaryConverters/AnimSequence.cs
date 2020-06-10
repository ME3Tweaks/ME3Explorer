﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gammtek.Conduit;
using Gammtek.Conduit.Extensions;
using ME3Explorer.Packages;
using ME3Explorer.Unreal.BinaryConverters;
using ME3Explorer.Unreal.ME3Enums;
using SharpDX;
using StreamHelpers;

namespace ME3Explorer.Unreal.BinaryConverters
{
    public class AnimSequence : ObjectBinary
    {
        public List<AnimTrack> RawAnimationData;
        public byte[] CompressedAnimationData;

        //for parsing the CompressedAnimationData

        private MEGame compressedDataSource;
        private int[] TrackOffsets;
        private List<string> boneList;
        private AnimationCompressionFormat rotCompression;
        private AnimationCompressionFormat posCompression;
        private AnimationKeyFormat keyEncoding;
        private int numFrames;

        protected override void Serialize(SerializingContainer2 sc)
        {
            if (sc.Game == MEGame.ME2)
            {
                int dummy = 0;
                sc.Serialize(ref dummy);
                sc.Serialize(ref dummy);
                sc.Serialize(ref dummy);
                int offset = sc.FileOffset + 4;
                sc.Serialize(ref offset);
            }

            if (sc.Game == MEGame.UDK)
            {
                if (sc.IsSaving && RawAnimationData is null)
                {
                    DecompressAnimationData();
                }
                sc.Serialize(ref RawAnimationData, SCExt.Serialize);
            }

            if (sc.IsLoading)
            {
                compressedDataSource = sc.Game;
                numFrames = Export.GetProperty<IntProperty>("NumFrames")?.Value ?? 0;
                TrackOffsets = Export.GetProperty<ArrayProperty<IntProperty>>("CompressedTrackOffsets").Select(i => i.Value).ToArray();
                if (compressedDataSource == MEGame.UDK)
                {
                    boneList = ((ExportEntry)Export.Parent)?.GetProperty<ArrayProperty<NameProperty>>("TrackBoneNames")?.Select(np => $"{np}").ToList();
                }
                else
                {
                    var animsetData = Export.GetProperty<ObjectProperty>("m_pBioAnimSetData");
                    //In ME2, BioAnimSetData can sometimes be in a different package. 
                    boneList = animsetData != null && Export.FileRef.IsUExport(animsetData.Value)
                        ? Export.FileRef.GetUExport(animsetData.Value)?.GetProperty<ArrayProperty<NameProperty>>("TrackBoneNames")?.Select(np => $"{np}").ToList()
                        : null;
                }

                boneList ??= Enumerable.Repeat("???", TrackOffsets.Length / 4).ToList();
                Enum.TryParse(Export.GetProperty<EnumProperty>("KeyEncodingFormat")?.Value.Name, out keyEncoding);
                Enum.TryParse(Export.GetProperty<EnumProperty>("RotationCompressionFormat")?.Value.Name, out rotCompression);
                Enum.TryParse(Export.GetProperty<EnumProperty>("TranslationCompressionFormat")?.Value.Name, out posCompression);
            }

            sc.Serialize(ref CompressedAnimationData, SCExt.Serialize);
        }

        public void UpdateProps(PropertyCollection props, MEGame newGame)
        {
            if (newGame != compressedDataSource && !(newGame <= MEGame.ME3 && compressedDataSource <= MEGame.ME3))
            {
                CompressAnimationData(props, newGame);
            }
        }

        void DecompressAnimationData()
        {
            var ms = new MemoryStream(CompressedAnimationData);
            RawAnimationData = new List<AnimTrack>();

            for (int i = 0; i < boneList.Count; i++)
            {
                int posOff = TrackOffsets[i * 4];
                int numPosKeys = TrackOffsets[i * 4 + 1];
                int rotOff = TrackOffsets[i * 4 + 2];
                int numRotKeys = TrackOffsets[i * 4 + 3];

                var track = new AnimTrack
                {
                    Positions = new List<Vector3>(numPosKeys),
                    Rotations = new List<Quaternion>(numRotKeys)
                };

                if (numPosKeys > 0)
                {
                    ms.JumpTo(posOff);

                    AnimationCompressionFormat compressionFormat = posCompression;

                    if (numPosKeys == 1)
                    {
                        compressionFormat = AnimationCompressionFormat.ACF_None;
                    }

                    for (int j = 0; j < numPosKeys; j++)
                    {
                        switch (compressionFormat)
                        {
                            case AnimationCompressionFormat.ACF_None:
                            case AnimationCompressionFormat.ACF_Float96NoW:
                                track.Positions.Add(new Vector3(ms.ReadFloat(), ms.ReadFloat(), ms.ReadFloat()));
                                break;
                            case AnimationCompressionFormat.ACF_IntervalFixed32NoW:
                            case AnimationCompressionFormat.ACF_Fixed48NoW:
                            case AnimationCompressionFormat.ACF_Fixed32NoW:
                            case AnimationCompressionFormat.ACF_Float32NoW:
                            case AnimationCompressionFormat.ACF_BioFixed48:
                                throw new NotImplementedException($"Translation keys in format {compressionFormat} cannot be read yet!");
                        }
                    }

                    if (keyEncoding == AnimationKeyFormat.AKF_VariableKeyLerp && numPosKeys > 1)
                    {
                        ms.JumpTo(ms.Position.Align(4));

                        var keyTimes = new List<int>(numPosKeys);
                        for (int j = 0; j < numPosKeys; j++)
                        {
                            keyTimes.Add(numFrames > 0xFF ? ms.ReadUInt16() : ms.ReadByte());
                        }
                        //RawAnimationData should have either 1 key, or the same number of keys as frames.
                        //Lerp any missing keys
                        List<Vector3> tempPositions = track.Positions;
                        track.Positions = new List<Vector3>(numFrames)
                        {
                            tempPositions[0]
                        };
                        for (int frameIdx = 1, keyIdx = 1; frameIdx < numFrames; keyIdx++, frameIdx++)
                        {
                            if (keyIdx >= keyTimes.Count)
                            {
                                track.Positions.Add(track.Positions[frameIdx - 1]);
                            }
                            else if (keyTimes[keyIdx] == frameIdx)
                            {
                                track.Positions.Add(tempPositions[keyIdx]);
                            }
                            else
                            {
                                int nextFrame = keyTimes[keyIdx];
                                int prevFrame = frameIdx - 1;
                                for (int j = frameIdx; j < nextFrame; j++)
                                {
                                    float amount = (float)(j - prevFrame) / (nextFrame - prevFrame);
                                    track.Positions.Add(Vector3.Lerp(track.Positions[prevFrame], tempPositions[keyIdx], amount));
                                }
                                track.Positions.Add(tempPositions[keyIdx]);
                                frameIdx = nextFrame;
                            }
                        }
                    }
                }

                if (numRotKeys > 0)
                {
                    ms.JumpTo(rotOff);

                    AnimationCompressionFormat compressionFormat = rotCompression;

                    if (numRotKeys == 1)
                    {
                        compressionFormat = AnimationCompressionFormat.ACF_Float96NoW;
                    }
                    else if (compressedDataSource != MEGame.UDK)
                    {
                        ms.Skip(12 * 2); //skip mins and ranges
                    }

                    for (int j = 0; j < numRotKeys; j++)
                    {
                        switch (compressionFormat)
                        {
                            case AnimationCompressionFormat.ACF_None:
                                track.Rotations.Add(new Quaternion(ms.ReadFloat(), ms.ReadFloat(), ms.ReadFloat(), ms.ReadFloat()));
                                break;
                            case AnimationCompressionFormat.ACF_Float96NoW:
                            {
                                float x = ms.ReadFloat();
                                float y = ms.ReadFloat();
                                float z = ms.ReadFloat();
                                track.Rotations.Add(new Quaternion(x, y, z, getW(x, y, z)));
                                break;
                            }
                            case AnimationCompressionFormat.ACF_BioFixed48:
                            {
                                const float shift = 0.70710678118f;
                                const float scale = 1.41421356237f;
                                const float precisionMult = 32767.0f;
                                ushort a = ms.ReadUInt16();
                                ushort b = ms.ReadUInt16();
                                ushort c = ms.ReadUInt16();
                                float x = (a & 0x7FFF) / precisionMult * scale - shift;
                                float y = (b & 0x7FFF) / precisionMult * scale - shift;
                                float z = (c & 0x7FFF) / precisionMult * scale - shift;
                                float w = getW(x, y, z);
                                int wPos = ((a >> 14) & 2) | ((b >> 15) & 1);
                                track.Rotations.Add(wPos switch
                                {
                                    0 => new Quaternion(w, x, y, z),
                                    1 => new Quaternion(x, w, y, z),
                                    2 => new Quaternion(x, y, w, z),
                                    _ => new Quaternion(x, y, z, w)
                                });
                                
                                break;
                            }
                            case AnimationCompressionFormat.ACF_Fixed48NoW:
                            {

                                const float scale = 32767.0f;
                                const ushort shift = 32767;
                                float x = (ms.ReadUInt16() - shift) / scale;
                                float y = (ms.ReadUInt16() - shift) / scale;
                                float z = (ms.ReadUInt16() - shift) / scale;
                                track.Rotations.Add(new Quaternion(x, y, z, getW(x, y, z)));
                                break;
                            }
                            case AnimationCompressionFormat.ACF_IntervalFixed32NoW:
                            case AnimationCompressionFormat.ACF_Fixed32NoW:
                            case AnimationCompressionFormat.ACF_Float32NoW:
                                throw new NotImplementedException($"Rotation keys in format {compressionFormat} cannot be read yet!");
                        }
                    }

                    if (keyEncoding == AnimationKeyFormat.AKF_VariableKeyLerp && numRotKeys > 1)
                    {
                        ms.JumpTo(ms.Position.Align(4));

                        var keyTimes = new List<int>(numRotKeys);
                        for (int j = 0; j < numRotKeys; j++)
                        {
                            keyTimes.Add(numFrames > 0xFF ? ms.ReadUInt16() : ms.ReadByte());
                        }
                        //RawAnimationData should have either 1 key, or the same number of keys as frames.
                        //Slerp any missing keys
                        List<Quaternion> tempRotations = track.Rotations;
                        track.Rotations = new List<Quaternion>(numFrames)
                        {
                            tempRotations[0]
                        };
                        for (int frameIdx = 1, keyIdx = 1; frameIdx < numFrames; keyIdx++, frameIdx++)
                        {
                            if (keyIdx >= keyTimes.Count)
                            {
                                track.Rotations.Add(track.Rotations[frameIdx - 1]);
                            }
                            else if (keyTimes[keyIdx] == frameIdx)
                            {
                                track.Rotations.Add(tempRotations[keyIdx]);
                            }
                            else
                            {
                                int nextFrame = keyTimes[keyIdx];
                                int prevFrame = frameIdx - 1;
                                for (int j = frameIdx; j < nextFrame; j++)
                                {
                                    float amount = (float)(j - prevFrame) / (nextFrame - prevFrame);
                                    track.Rotations.Add(Quaternion.Slerp(track.Rotations[prevFrame], tempRotations[keyIdx], amount));
                                }
                                track.Rotations.Add(tempRotations[keyIdx]);
                                frameIdx = nextFrame;
                            }
                        }
                    }
                }

                RawAnimationData.Add(track);
            }
            static float getW(float x, float y, float z)
            {
                float wSquared = 1.0f - (x * x + y * y + z * z);
                return (float)(wSquared > 0 ? Math.Sqrt(wSquared) : 0);
            }
        }

        void CompressAnimationData(PropertyCollection props, MEGame game, AnimationCompressionFormat newRotationCompression = AnimationCompressionFormat.ACF_Float96NoW)
        {
            if (RawAnimationData is null)
            {
                DecompressAnimationData();
            }

            keyEncoding = AnimationKeyFormat.AKF_ConstantKeyLerp;
            posCompression = AnimationCompressionFormat.ACF_None;
            rotCompression = newRotationCompression;
            TrackOffsets = new int[boneList.Count * 4];
            var ms = new MemoryStream();

            for (int i = 0; i < boneList.Count; i++)
            {
                AnimTrack track = RawAnimationData[i];

                TrackOffsets[i * 4] = (int)ms.Position;
                int numPosKeys = track.Positions.Count;
                TrackOffsets[i * 4 + 1] = numPosKeys;

                if (numPosKeys > 0)
                {
                    AnimationCompressionFormat compressionFormat = posCompression;

                    if (numPosKeys == 1)
                    {
                        compressionFormat = AnimationCompressionFormat.ACF_None;
                    }

                    for (int j = 0; j < numPosKeys; j++)
                    {
                        switch (compressionFormat)
                        {
                            case AnimationCompressionFormat.ACF_None:
                            case AnimationCompressionFormat.ACF_Float96NoW:
                                ms.WriteFloat(track.Positions[j].X);
                                ms.WriteFloat(track.Positions[j].Y);
                                ms.WriteFloat(track.Positions[j].Z);
                                break;
                            default:
                                throw new NotImplementedException($"Translation keys in format {compressionFormat} cannot be read yet!");
                        }
                    }
                    PadTo4();
                }

                TrackOffsets[i * 4 + 2] = (int)ms.Position;
                int numRotKeys = track.Rotations.Count;
                TrackOffsets[i * 4 + 3] = numRotKeys;

                if (numRotKeys > 0)
                {
                    AnimationCompressionFormat compressionFormat = rotCompression;

                    if (numRotKeys == 1)
                    {
                        compressionFormat = AnimationCompressionFormat.ACF_Float96NoW;
                    }
                    else if (game != MEGame.UDK)
                    {
                        float xMin, yMin, zMin, xMax, yMax, zMax;
                        xMin = yMin = zMin = float.MaxValue;
                        xMax = yMax = zMax = float.MinValue;

                        foreach (Quaternion quat in track.Rotations)
                        {
                            xMin = Math.Min(xMin, quat.X);
                            yMin = Math.Min(yMin, quat.Y);
                            zMin = Math.Min(zMin, quat.Z);
                            xMax = Math.Max(xMax, quat.X);
                            yMax = Math.Max(yMax, quat.Y);
                            zMax = Math.Max(zMax, quat.Z);
                        }

                        ms.WriteFloat(xMin);
                        ms.WriteFloat(yMin);
                        ms.WriteFloat(zMin);
                        ms.WriteFloat(xMax - xMin);
                        ms.WriteFloat(yMax - yMin);
                        ms.WriteFloat(zMax - zMin);
                    }

                    for (int j = 0; j < numRotKeys; j++)
                    {
                        Quaternion rot = track.Rotations[j];
                        switch (compressionFormat)
                        {
                            case AnimationCompressionFormat.ACF_None:
                                ms.WriteFloat(rot.X);
                                ms.WriteFloat(rot.Y);
                                ms.WriteFloat(rot.Z);
                                ms.WriteFloat(rot.W);
                                break;
                            case AnimationCompressionFormat.ACF_Float96NoW:
                                ms.WriteFloat(rot.X);
                                ms.WriteFloat(rot.Y);
                                ms.WriteFloat(rot.Z);
                                break;
                            case AnimationCompressionFormat.ACF_BioFixed48:
                                {
                                    const float shift = 0.70710678118f;
                                    const float scale = 1.41421356237f;
                                    const float precisionMult = 32767.0f;
                                    //smallest three compression: https://gafferongames.com/post/snapshot_compression/
                                    //omit the largest component, and store its index
                                    int wPos = 0;
                                    float max = 0f;
                                    for (int k = 0; k < 4; k++)
                                    {
                                        if (Math.Abs(rot[k]) > max)
                                        {
                                            max = Math.Abs(rot[k]);
                                            wPos = k;
                                        }
                                    }
                                    //remap the smallest three components to the lower 15 bits of a ushort
                                    ushort a, b, c;

                                    static ushort compress(float f) => (ushort)((f + shift) / scale * precisionMult).Clamp(0, 0x7FFF);

                                    switch (wPos)
                                    {
                                        case 0:
                                            a = compress(rot.Y);
                                            b = compress(rot.Z);
                                            c = compress(rot.W);
                                            break;
                                        case 1:
                                            a = compress(rot.X);
                                            b = compress(rot.Z);
                                            c = compress(rot.W);
                                            break;
                                        case 2:
                                            a = compress(rot.X);
                                            b = compress(rot.Y);
                                            c = compress(rot.W);
                                            break;
                                        default:
                                            a = compress(rot.X);
                                            b = compress(rot.Y);
                                            c = compress(rot.Z);
                                            break;
                                    }

                                    //stuff the 2 bit index of the omitted component into the high bits of a and b
                                    a |= (ushort)((wPos & 2) << 14);
                                    b |= (ushort)((wPos & 1) << 15);

                                    ms.WriteUInt16(a);
                                    ms.WriteUInt16(b);
                                    ms.WriteUInt16(c);
                                }
                                break;
                            case AnimationCompressionFormat.ACF_Fixed48NoW:
                            {

                                const float scale = 32767.0f;
                                const ushort shift = 32767;
                                ms.WriteUInt16((ushort)(rot.X * scale + shift).Clamp(0, ushort.MaxValue));
                                ms.WriteUInt16((ushort)(rot.Y * scale + shift).Clamp(0, ushort.MaxValue));
                                ms.WriteUInt16((ushort)(rot.Z * scale + shift).Clamp(0, ushort.MaxValue));
                                break;
                            }
                            case AnimationCompressionFormat.ACF_IntervalFixed32NoW:
                            case AnimationCompressionFormat.ACF_Fixed32NoW:
                            case AnimationCompressionFormat.ACF_Float32NoW:
                            default:
                                throw new NotImplementedException($"Rotation keys in format {compressionFormat} cannot be read!");
                        }
                    }
                    PadTo4();
                }
            }

            CompressedAnimationData = ms.ToArray();
            compressedDataSource = game;

            props.RemoveNamedProperty("KeyEncodingFormat");
            props.RemoveNamedProperty("TranslationCompressionFormat");
            props.AddOrReplaceProp(new EnumProperty(rotCompression.ToString(), nameof(AnimationCompressionFormat), game, "RotationCompressionFormat"));
            props.AddOrReplaceProp(new ArrayProperty<IntProperty>(TrackOffsets.Select(i => new IntProperty(i)), "CompressedTrackOffsets"));

            void PadTo4()
            {
                var numAlignBytes = ms.Position.Align(4) - ms.Position;
                for (int j = 0; j < numAlignBytes; j++)
                {
                    ms.WriteByte(0x55);
                }
            }
        }
    }

    public class AnimTrack
    {
        public List<Vector3> Positions;
        public List<Quaternion> Rotations;
    }
}
namespace ME3Explorer
{
    public static partial class SCExt
    {
        public static void Serialize(this SerializingContainer2 sc, ref AnimTrack track)
        {
            if (sc.IsLoading)
            {
                track = new AnimTrack();
            }

            int vector3Size = 12;
            sc.Serialize(ref vector3Size);
            sc.Serialize(ref track.Positions, Serialize);
            int quatSize = 16;
            sc.Serialize(ref quatSize);
            sc.Serialize(ref track.Rotations, Serialize);
        }
    }
}
