using System.Buffers.Binary;

namespace OBP.PS2.Geometry;

public static partial class GcUyaMoby
{

    /// <summary>Fully decoded packet vertices in emitted vertex order.</summary>
    private sealed record PacketVertices(
        List<double> X, List<double> Y, List<double> Z,
        List<double> LocalX, List<double> LocalY, List<double> LocalZ,
        List<float> S, List<float> T,
        List<(float X, float Y, float Z)> Normals,
        List<(int J0, int J1, int J2, float W0, float W1, float W2)> Bindings);

    /// <summary>
    /// Preserve distinct legacy static bind and native persistent matrix-slot
    /// reconstruction. Only the low nine strip-index bits are pipelined.
    /// </summary>
    private static PacketVertices ReconstructVertices(DecodeState state, PacketData packet)
    {
        byte[] buf = state.Buffer;
        int eo = packet.EntryOffset, vh = packet.HeaderOffset, vbase = packet.VertexBase;
        int matrixTransferCount = packet.MatrixTransferCount;
        int twoWay = packet.TwoWay, threeWay = packet.ThreeWay;
        int inFileCount = packet.VertexCount, dupCount = packet.DuplicateCount;
        int vertexTableOffset = packet.VertexTableOffset;
        var sts = packet.Sts;
        double k = state.K, skinCap = state.SkinCap;
        int jointCount = state.JointCount;
        double[][]? globalBind = state.GlobalBind;
        var skinState = state.SkinState;
        var vertexCache = state.VertexCache;
        bool recoverPersistentVertexCache = state.RecoverPersistentVertexCache;
        bool applySkin = globalBind is not null;
        var legacySkinAttrs = applySkin
            ? ReadPacketSkinLegacy(buf, vh, vbase, matrixTransferCount, twoWay, threeWay, inFileCount)
            : null;
        var animationSkinAttrs = applySkin
            ? ReadPacketSkin(buf, vh, vbase, matrixTransferCount, twoWay, threeWay, inFileCount, jointCount, skinState)
            : null;

        var rawX = new int[inFileCount];
        var rawY = new int[inFileCount];
        var rawZ = new int[inFileCount];
        var rawIdx = new int[inFileCount];
        var norm = new (float X, float Y, float Z)[inFileCount];
        for (int v = 0; v < inFileCount; v++)
        {
            int o = vbase + v * 0x10;
            rawIdx[v] = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(o)) & 0x1ff;
            rawX[v] = BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(o + 0x0a));
            rawY[v] = BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(o + 0x0c));
            rawZ[v] = BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(o + 0x0e));
            // Normal: spherical (azimuth @0x08, elevation @0x09), unit,
            // byte * PI/128. Not pipelined (like the position).
            double az = buf[o + 0x08] * (System.Math.PI / 128.0);
            double el = buf[o + 0x09] * (System.Math.PI / 128.0);
            norm[v] = (
                (float)(System.Math.Sin(az) * System.Math.Cos(el)),
                (float)(System.Math.Cos(az) * System.Math.Cos(el)),
                (float)System.Math.Sin(el));
        }

        for (int i = VertexPipeline; i < inFileCount; i++)
        {
            rawIdx[i - VertexPipeline] = rawIdx[i];
        }

        if (recoverPersistentVertexCache)
        {
            // The last seven VU1 slot indices are written into trailing
            // epilogue records, then into six halfwords in the last one.
            // The old path retained unrelated skin bits as slot IDs.
            int epilogueRecords = buf[eo + 0x0c] - vertexTableOffset / 16 - inFileCount;
            long epilogueEnd = (long)vbase + (inFileCount + epilogueRecords) * 16;
            if (epilogueRecords is < 1 or > 6 || epilogueEnd > buf.Length)
                throw new InvalidDataException("Invalid GC/UYA Moby vertex-index epilogue.");

            for (int i = System.Math.Max(VertexPipeline - inFileCount, 0);
                i < epilogueRecords; i++)
            {
                int dest = inFileCount + i - VertexPipeline;
                if (dest >= 0 && dest < inFileCount)
                    rawIdx[dest] = BinaryPrimitives.ReadUInt16LittleEndian(
                        buf.AsSpan(vbase + (inFileCount + i) * 16)) & 0x1ff;
            }

            int finalRecord = vbase + (inFileCount + epilogueRecords - 1) * 16;
            for (int i = System.Math.Max(VertexPipeline - inFileCount - epilogueRecords, 0);
                i < 6; i++)
            {
                int dest = inFileCount + epilogueRecords + i - VertexPipeline;
                if (dest >= 0 && dest < inFileCount)
                    rawIdx[dest] = BinaryPrimitives.ReadUInt16LittleEndian(
                        buf.AsSpan(finalRecord + 4 + i * 2)) & 0x1ff;
            }
        }

        var posX = new List<double>();
        var posY = new List<double>();
        var posZ = new List<double>();
        bool packetSkinBlewUp = false;
        for (int v = 0; v < inFileCount; v++)
        {
            SkinAttr? attr = legacySkinAttrs?[v];
            if (globalBind is null || attr is not { } at || at.Count == 0)
            {
                posX.Add(rawX[v] * k);
                posY.Add(rawY[v] * k);
                posZ.Add(rawZ[v] * k);
                continue;
            }

            double sx = 0, sy = 0, sz = 0, tw = 0;
            Span<int> js = [at.J0, at.J1, at.J2];
            Span<int> ws = [at.W0, at.W1, at.W2];
            for (int j = 0; j < at.Count; j++)
            {
                int w = ws[j];
                if (w == 0)
                {
                    continue;
                }

                int ji = js[j];
                var m = ji >= 0 && ji < globalBind.Length ? globalBind[ji] : AffineId;
                var (qx, qy, qz) = AffineApply(m, rawX[v], rawY[v], rawZ[v]);
                sx += w * qx;
                sy += w * qy;
                sz += w * qz;
                tw += w;
            }

            if (tw == 0)
            {
                var m0 = at.J0 >= 0 && at.J0 < globalBind.Length ? globalBind[at.J0] : AffineId;
                var (qx, qy, qz) = AffineApply(m0, rawX[v], rawY[v], rawZ[v]);
                sx = qx;
                sy = qy;
                sz = qz;
                tw = 1;
            }

            double fx = sx / tw * k, fy = sy / tw * k, fz = sz / tw * k;
            if (!double.IsFinite(fx) || !double.IsFinite(fy) || !double.IsFinite(fz) ||
                System.Math.Abs(fx) > skinCap || System.Math.Abs(fy) > skinCap || System.Math.Abs(fz) > skinCap)
            {
                packetSkinBlewUp = true;
                break;
            }

            posX.Add(fx);
            posY.Add(fy);
            posZ.Add(fz);
        }

        if (packetSkinBlewUp)
        {
            posX.Clear();
            posY.Clear();
            posZ.Clear();
            for (int v = 0; v < inFileCount; v++)
            {
                posX.Add(rawX[v] * k);
                posY.Add(rawY[v] * k);
                posZ.Add(rawZ[v] * k);
            }
        }
        else if (applySkin && twoWay + threeWay > 0)
        {
            state.SkinningApplied = true;
        }

        int arrayOfs = vh + 0x10 + matrixTransferCount * 2;
        if (arrayOfs % 4 != 0) arrayOfs += 2;
        if (arrayOfs % 8 != 0) arrayOfs += 4;
        var dupes = new List<int>();
        for (int d = 0; d < dupCount; d++)
        {
            int o = arrayOfs + d * 2;
            if (o + 2 > buf.Length)
            {
                break;
            }

            dupes.Add(BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(o)) >> 7);
        }

        var listX = new List<double>();
        var listY = new List<double>();
        var listZ = new List<double>();
        var listLocalX = new List<double>();
        var listLocalY = new List<double>();
        var listLocalZ = new List<double>();
        var listS = new List<float>();
        var listT = new List<float>();
        var listN = new List<(float X, float Y, float Z)>();
        var listJW = new List<(int J0, int J1, int J2, float W0, float W1, float W2)>();
        if (!recoverPersistentVertexCache)
            vertexCache.Clear();

        static (int, int, int, float, float, float) Bind(SkinAttr? attr)
        {
            if (attr is not { } a || a.Count == 0)
            {
                return (0, 0, 0, 1f, 0f, 0f);
            }

            float w0 = a.W0, w1 = a.Count > 1 ? a.W1 : 0f, w2 = a.Count > 2 ? a.W2 : 0f;
            float sum = w0 + w1 + w2;
            if (sum <= 0f)
            {
                return (a.J0, 0, 0, 1f, 0f, 0f);
            }

            return (a.J0, a.J1, a.J2, w0 / sum, w1 / sum, w2 / sum);
        }

        for (int v = 0; v < inFileCount; v++)
        {
            int pos = listX.Count;
            listX.Add(posX[v]);
            listY.Add(posY[v]);
            listZ.Add(posZ[v]);
            listLocalX.Add(rawX[v] * k);
            listLocalY.Add(rawY[v] * k);
            listLocalZ.Add(rawZ[v] * k);
            listN.Add(norm[v]);
            listJW.Add(Bind(animationSkinAttrs?[v]));
            var st = v < sts.Count ? sts[v] : (0, 0);
            listS.Add(st.Item1 / 4096f);
            listT.Add(st.Item2 / 4096f);
            vertexCache[rawIdx[v]] = new CachedVertex(
                listX[pos], listY[pos], listZ[pos],
                listLocalX[pos], listLocalY[pos], listLocalZ[pos],
                listN[pos], listJW[pos]);
        }

        for (int di = 0; di < dupes.Count; di++)
        {
            if (!vertexCache.TryGetValue(dupes[di], out var from))
            {
                if (recoverPersistentVertexCache)
                    throw new InvalidDataException("Unresolved Moby duplicate VU1 vertex-cache slot.");
                continue;
            }

            listX.Add(from.X);
            listY.Add(from.Y);
            listZ.Add(from.Z);
            listLocalX.Add(from.LocalX);
            listLocalY.Add(from.LocalY);
            listLocalZ.Add(from.LocalZ);
            listN.Add(from.Normal);
            listJW.Add(from.Binding);
            var st = inFileCount + di < sts.Count ? sts[inFileCount + di] : (0, 0);
            listS.Add(st.Item1 / 4096f);
            listT.Add(st.Item2 / 4096f);
        }


        return new PacketVertices(listX, listY, listZ, listLocalX, listLocalY,
            listLocalZ, listS, listT, listN, listJW);
    }

}
