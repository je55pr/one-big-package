using System.Buffers.Binary;

namespace OBP.PS2.Geometry;

public static partial class GcUyaMoby
{
    /// <summary>
    /// Legacy static-render binding simulation. It intentionally preserves the
    /// historical per-packet reset / seven-ahead upper-bit heuristic so this
    /// animation milestone does not silently rewrite established world geometry.
    /// </summary>
    private static SkinAttr[] ReadPacketSkinLegacy(
        byte[] buf, int vh, int vbase, int matrixTransferCount, int twoWay, int threeWay, int count)
    {
        var blend = new SkinAttr?[64];

        void Set(int addr, SkinAttr a)
        {
            if (addr != 0xf4)
            {
                blend[(addr & 0xff) >> 2] = a;
            }
        }

        SkinAttr Get(int addr) =>
            blend[(addr & 0xff) >> 2] ?? new SkinAttr { Count = 1, J0 = 0, J1 = 0, J2 = 0, W0 = 255, W1 = 0, W2 = 0 };

        for (int t = 0; t < matrixTransferCount; t++)
        {
            int o = vh + 0x10 + t * 2;
            if (o + 2 > buf.Length)
            {
                break;
            }

            Set(buf[o + 1], new SkinAttr { Count = 1, J0 = buf[o], W0 = 255 });
        }

        var attrs = new SkinAttr[count];
        for (int v = 0; v < count; v++)
        {
            int o = vbase + v * 0x10;
            int sprOffset = vbase + System.Math.Min(count - 1, v + VertexPipeline) * 0x10;
            int sprJoint = (BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(sprOffset)) & 0xfe00) >> 9;
            int B(int k) => buf[o + k];

            SkinAttr a;
            if (v < twoWay)
            {
                Set(B(6), new SkinAttr { Count = 1, J0 = sprJoint, W0 = 255 });
                var s1 = Get(B(2));
                var s2 = Get(B(3));
                a = new SkinAttr { Count = 2, J0 = s1.J0, J1 = s2.J0, J2 = 0, W0 = B(4), W1 = B(5), W2 = 0 };
                Set(B(7), a);
            }
            else if (v < twoWay + threeWay)
            {
                var s1 = Get(B(2));
                var s2 = Get(B(3));
                var s3 = Get((sprJoint * 2) & 0xff);
                a = new SkinAttr { Count = 3, J0 = s1.J0, J1 = s2.J0, J2 = s3.J0, W0 = B(4), W1 = B(5), W2 = B(6) };
                Set(B(7), a);
            }
            else
            {
                Set(B(3), new SkinAttr { Count = 1, J0 = sprJoint, W0 = 255 });
                a = Get(B(2));
            }

            attrs[v] = a;
        }

        return attrs;
    }

    private static SkinAttr[] ReadPacketSkin(
        byte[] buf, int vh, int vbase, int matrixTransferCount, int twoWay, int threeWay,
        int count, int jointCount, SkinState state)
    {
        void Reject() => state.FullyResolved = false;
        bool ValidAddress(int address)
        {
            bool valid = (address & 3) == 0;
            if (!valid) Reject();
            return valid;
        }
        SkinAttr Rigid(int joint)
        {
            if (joint < 0 || joint >= jointCount)
            {
                Reject();
                joint = 0;
            }
            return new SkinAttr { Count = 1, J0 = joint, W0 = 256 };
        }
        void Set(int address, SkinAttr attr)
        {
            if (ValidAddress(address)) state.Slots[address >> 2] = attr;
        }
        SkinAttr Get(int address, bool requireRigid = false)
        {
            if (!ValidAddress(address) || state.Slots[address >> 2] is not { } attr)
            {
                Reject();
                return Rigid(0);
            }
            if (requireRigid && attr.Count != 1) Reject();
            return attr;
        }
        for (int t = 0; t < matrixTransferCount; t++)
        {
            int o = vh + 0x10 + t * 2;
            if (o + 2 > buf.Length) { Reject(); break; }
            Set(buf[o + 1], Rigid(buf[o]));
        }

        var attrs = new SkinAttr[count];
        for (int v = 0; v < count; v++)
        {
            int o = vbase + v * 0x10;
            int upperBits = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(o)) >> 9;
            int B(int k) => buf[o + k];
            SkinAttr attr;
            if (v < twoWay)
            {
                Set(B(6), Rigid(upperBits));
                var a = Get(B(2), requireRigid: true);
                var b = Get(B(3), requireRigid: true);
                if (B(4) + B(5) != 256) Reject();
                attr = new SkinAttr { Count = 2, J0 = a.J0, J1 = b.J0, W0 = B(4), W1 = B(5) };
                Set(B(7), attr);
            }
            else if (v < twoWay + threeWay)
            {
                var a = Get(B(2), requireRigid: true);
                var b = Get(B(3), requireRigid: true);
                var c = Get(upperBits * 2, requireRigid: true);
                if (B(4) + B(5) + B(6) != 256) Reject();
                attr = new SkinAttr
                {
                    Count = 3,
                    J0 = a.J0,
                    J1 = b.J0,
                    J2 = c.J0,
                    W0 = B(4),
                    W1 = B(5),
                    W2 = B(6),
                };
                Set(B(7), attr);
            }
            else
            {
                Set(B(3), Rigid(upperBits));
                attr = Get(B(2));
            }
            attrs[v] = attr;
        }
        return attrs;
    }
}
