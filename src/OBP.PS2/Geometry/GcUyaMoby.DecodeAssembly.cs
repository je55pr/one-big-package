namespace OBP.PS2.Geometry;

public static partial class GcUyaMoby
{

    /// <summary>
    /// Assemble GS material-switch strips and triangles without normalizing
    /// indices, winding, degeneracy or emitted ordering.
    /// </summary>
    private static void AppendPacketGeometry(
        DecodeState state, PacketData packet, PacketVertices vertices)
    {
        var idxBuf = packet.StripIndices;
        var secretIndices = packet.SecretIndices;
        var textures = packet.Textures;
        int material = state.Material, materialStateIndex = state.MaterialStateIndex;
        var positions = state.Positions;
        var skinLocalPositionsOut = state.SkinLocalPositions;
        var uvs = state.Uvs;
        var normalsOut = state.Normals;
        var vertexJointsOut = state.VertexJoints;
        var vertexWeightsOut = state.VertexWeights;
        var indices = state.Indices;
        var triangleMaterialSlots = state.TriangleMaterialSlots;
        var triangleMaterialStateIndices = state.TriangleMaterialStateIndices;
        var listX = vertices.X;
        var listY = vertices.Y;
        var listZ = vertices.Z;
        var listLocalX = vertices.LocalX;
        var listLocalY = vertices.LocalY;
        var listLocalZ = vertices.LocalZ;
        var listS = vertices.S;
        var listT = vertices.T;
        var listN = vertices.Normals;
        var listJW = vertices.Bindings;
        var prims = new List<MPrim>();
        MPrim? prim = null;
        int adGif = 0;
        for (int j = 0; j < idxBuf.Count; j++)
        {
            int index = idxBuf[j];
            if (index == 0)
            {
                int secret = adGif < secretIndices.Count ? secretIndices[adGif] : 0;
                if (secret == 0)
                {
                    if (prim is { Strip.Count: >= 3 })
                    {
                        prim.Strip.RemoveRange(prim.Strip.Count - 3, 3);
                    }

                    break;
                }

                index = secret - 0x80;
                if (adGif < textures.Count)
                {
                    material = textures[adGif].TextureId;
                    materialStateIndex = textures[adGif].MaterialStateIndex;
                }
                adGif++;
            }

            if (index <= 0)
            {
                if (j + 1 < idxBuf.Count && idxBuf[j + 1] <= 0)
                {
                    prim = new MPrim { Material = material, MaterialStateIndex = materialStateIndex };
                    prims.Add(prim);
                }
                else if (prim is { Strip.Count: >= 1 })
                {
                    prim.Strip.Add(prim.Strip[^1]);
                }
            }

            if (prim is null)
            {
                continue;
            }

            prim.Strip.Add((index & 0x7f) - 1);
        }

        int vbaseOut = positions.Count / 3;
        for (int v = 0; v < listX.Count; v++)
        {
            positions.Add(listX[v]);
            positions.Add(listY[v]);
            positions.Add(listZ[v]);
            skinLocalPositionsOut.Add(listLocalX[v]);
            skinLocalPositionsOut.Add(listLocalY[v]);
            skinLocalPositionsOut.Add(listLocalZ[v]);
            uvs.Add(listS[v]);
            uvs.Add(listT[v]);
            normalsOut.Add(listN[v].X);
            normalsOut.Add(listN[v].Y);
            normalsOut.Add(listN[v].Z);
            var jw = listJW[v];
            vertexJointsOut.Add(jw.J0);
            vertexJointsOut.Add(jw.J1);
            vertexJointsOut.Add(jw.J2);
            vertexWeightsOut.Add(jw.W0);
            vertexWeightsOut.Add(jw.W1);
            vertexWeightsOut.Add(jw.W2);
        }

        foreach (var pr in prims)
        {
            for (int i = 0; i + 3 <= pr.Strip.Count; i++)
            {
                int a = pr.Strip[i], b = pr.Strip[i + 1], c = pr.Strip[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= listX.Count || b >= listX.Count || c >= listX.Count)
                {
                    continue;
                }

                if (a == b || b == c || a == c)
                {
                    continue;
                }

                if (i % 2 == 0)
                {
                    indices.Add(vbaseOut + a);
                    indices.Add(vbaseOut + b);
                    indices.Add(vbaseOut + c);
                }
                else
                {
                    indices.Add(vbaseOut + b);
                    indices.Add(vbaseOut + a);
                    indices.Add(vbaseOut + c);
                }

                triangleMaterialSlots.Add(pr.Material);
                triangleMaterialStateIndices.Add(pr.MaterialStateIndex);
            }
        }
        state.Material = material;
        state.MaterialStateIndex = materialStateIndex;
    }

}
