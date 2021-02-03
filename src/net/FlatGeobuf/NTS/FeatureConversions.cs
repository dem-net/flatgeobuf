using System;
using System.Linq;
using System.Collections.Generic;
using NetTopologySuite.Features;
using FlatBuffers;
using System.Text;
using System.IO;
using NTSGeometry = NetTopologySuite.Geometries.Geometry;
using System.Runtime.InteropServices;

namespace FlatGeobuf.NTS
{
    public static class FeatureConversions
    {
        public static byte[] ToByteBuffer(IFeature feature, GeometryType geometryType, byte dimensions, IList<ColumnMeta> columns)
        {
            var builder = new FlatBufferBuilder(4096);
            var go = GeometryConversions.BuildGeometry(builder, feature.Geometry, geometryType, dimensions);
            var memoryStream = new MemoryStream();
            if (feature.Attributes != null && feature.Attributes.Count > 0 && columns != null)
            {
                var writer = new BinaryWriter(memoryStream, Encoding.UTF8);
                for (ushort i = 0; i < columns.Count; i++)
                {
                    var column = columns[i];
                    var type = column.Type;
                    var name = column.Name;
                    if (!feature.Attributes.Exists(name))
                        continue;
                    var value = feature.Attributes[name];
                    if (value is null)
                        continue;
                    writer.Write(i);
                    switch (type)
                    {
                        case ColumnType.Bool:
                            writer.Write((bool) value);
                            break;
                        case ColumnType.Byte:
                            writer.Write((sbyte) value);
                            break;
                        case ColumnType.UByte:
                            writer.Write((byte) value);
                            break;
                        case ColumnType.Short:
                            writer.Write((short) value);
                            break;
                        case ColumnType.UShort:
                            writer.Write((ushort) value);
                            break;
                        case ColumnType.Int:
                            writer.Write((int) value);
                            break;
                        case ColumnType.UInt:
                            writer.Write((uint) value);
                            break;
                        case ColumnType.Long:
                            writer.Write((long) value);
                            break;
                        case ColumnType.ULong:
                            writer.Write((ulong) value);
                            break;
                        case ColumnType.Float:
                            writer.Write((float) value);
                            break;
                        case ColumnType.Double:
                            writer.Write((double) value);
                            break;
                        case ColumnType.String:
                            var bytes = Encoding.UTF8.GetBytes((string) value);
                            writer.Write(bytes.Length);
                            writer.Write(bytes);
                            break;
                        default:
                            throw new ApplicationException("Unknown type " + value.GetType().FullName);
                    }
                }
            }

            var propertiesOffset = default(VectorOffset);
            if (memoryStream.Position > 0)
                propertiesOffset = Feature.CreatePropertiesVectorBlock(builder, memoryStream.ToArray());

            Offset<Geometry> geometryOffset;
            if (go.gos != null && go.gos.Length > 0) {
                var partOffsets = new Offset<Geometry>[go.gos.Length];
                for (int i = 0; i < go.gos.Length; i++) {
                    var goPart = go.gos[i];
                    var partOffset = Geometry.CreateGeometry(builder, goPart.endsOffset, goPart.xyOffset, goPart.zOffset, goPart.mOffset, default, default, go.Type, default);
                    partOffsets[i] = partOffset;
                }
                var partsOffset = Geometry.CreatePartsVector(builder, partOffsets);
                geometryOffset = Geometry.CreateGeometry(builder, default, default, default, default, default, default, go.Type, partsOffset);
            } else {
                geometryOffset = Geometry.CreateGeometry(builder, go.endsOffset, go.xyOffset, go.zOffset, go.mOffset, default, default, go.Type, default);
            }
            Feature.StartFeature(builder);

            Feature.AddGeometry(builder, geometryOffset);
            Feature.AddProperties(builder, propertiesOffset);
            var featureOffset = Feature.EndFeature(builder);

            builder.FinishSizePrefixed(featureOffset.Value);

            return builder.DataBuffer.ToSizedArray();
        }

        public static IFeature FromByteBuffer(ByteBuffer bb, ref Header header)
        {
            var feature = Feature.GetRootAsFeature(bb);
            IAttributesTable attributesTable = null;
            if (feature.PropertiesLength != 0)
            {
                attributesTable = new AttributesTable();
                int pos = 0;
                var bytes = feature.GetPropertiesBytes();
                while (pos < feature.PropertiesLength)
                {
                    ushort i = MemoryMarshal.Read<ushort>(bytes.Slice(pos, 2));
                    pos += 2;
                    var column = header.Columns(i).Value;
                    var type = column.Type;
                    var name = column.Name;
                    switch (type)
                    {
                        case ColumnType.Bool:
                            attributesTable.Add(name, MemoryMarshal.Read<bool>(bytes.Slice(pos, 1)));
                            pos += 1;
                            break;
                        case ColumnType.Short:
                            attributesTable.Add(name, MemoryMarshal.Read<short>(bytes.Slice(pos, 2)));
                            pos += 1;
                            break;
                        case ColumnType.Int:
                            attributesTable.Add(name, MemoryMarshal.Read<int>(bytes.Slice(pos, 4)));
                            pos += 4;
                            break;
                        case ColumnType.Long:
                            attributesTable.Add(name, MemoryMarshal.Read<long>(bytes.Slice(pos, 8)));
                            pos += 8;
                            break;
                        case ColumnType.Double:
                            attributesTable.Add(name, MemoryMarshal.Read<double>(bytes.Slice(pos, 8)));
                            pos += 8;
                            break;
                        case ColumnType.Float:
                            attributesTable.Add(name, MemoryMarshal.Read<float>(bytes.Slice(pos, 4)));
                            pos += 4;
                            break;
                        case ColumnType.Byte:
                            attributesTable.Add(name, MemoryMarshal.Read<byte>(bytes.Slice(pos, 1)));
                            pos += 1;
                            break;
                        case ColumnType.DateTime:
                        case ColumnType.String:
                            int len = MemoryMarshal.Read<int>(bytes.Slice(pos, 4));
                            pos += 4;
                            attributesTable.Add(name, Encoding.UTF8.GetString(bytes.Slice(pos, len)));
                            pos += len;
                            break;
                        default: throw new Exception($"Unknown type {type}");
                    }
                }
            }

            NTSGeometry geometry = null;
            try
            {
                if (feature.Geometry.HasValue)
                {
                    var geometryCopy = feature.Geometry.Value;
                    geometry = GeometryConversions.FromFlatbuf(ref geometryCopy, ref header);
                }
            }
            catch (ArgumentException)
            {

            }

            var f = new NetTopologySuite.Features.Feature(geometry, attributesTable);
            return f;
        }
    }
}