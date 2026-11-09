using System.Buffers.Binary;
using System.Text;

namespace OscTester;

internal sealed record TrackingStatus(
    string? AvatarId = null, bool? AvatarSupport = null, bool? Face = null, bool? Eye = null);

internal static class OscPacket
{
    public const int FacePayloadSize = 102;

    private static ReadOnlySpan<byte> FaceHeader => "/tracking/face/v1\0\0\0,b\0\0"u8;

    public static byte[] CreateFacePacket(ReadOnlySpan<byte> values)
    {
        if (values.Length != FacePayloadSize)
            throw new ArgumentException("A face tracking frame requires 102 values.", nameof(values));

        // After the padded address and tags, write the blob length, payload, and two pad bytes.
        int payloadOffset = FaceHeader.Length + sizeof(int);
        var packet = new byte[payloadOffset + FacePayloadSize + 2];
        FaceHeader.CopyTo(packet);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(FaceHeader.Length), FacePayloadSize);
        values.CopyTo(packet.AsSpan(payloadOffset));
        return packet;
    }

    public static TrackingStatus ReadStatus(ReadOnlySpan<byte> packet, TrackingStatus status, int depth = 0)
    {
        if (packet.Length == 0 || packet.Length % 4 != 0 || depth > 8)
            return status;

        if (packet.StartsWith("#bundle\0"u8) && packet.Length >= 16)
        {
            packet = packet[16..]; // Bundle header and timetag
            while (packet.Length >= 4)
            {
                int size = BinaryPrimitives.ReadInt32BigEndian(packet);
                packet = packet[4..];
                if (size <= 0 || size % 4 != 0 || size > packet.Length)
                    break;

                status = ReadStatus(packet[..size], status, depth + 1);
                packet = packet[size..];
            }

            return status;
        }

        if (!TryReadString(ref packet, out var address) || !TryReadString(ref packet, out var tags))
            return status;

        if (address.SequenceEqual("/avatar/change"u8) && tags.SequenceEqual(",s"u8)
            && TryReadString(ref packet, out var avatarId) && !avatarId.IsEmpty && packet.IsEmpty)
        {
            return status with { AvatarId = Encoding.UTF8.GetString(avatarId), AvatarSupport = null };
        }

        if (!packet.IsEmpty)
            return status;

        bool value = tags.SequenceEqual(",T"u8);
        if (!value && !tags.SequenceEqual(",F"u8))
            return status;

        if (address.SequenceEqual("/avatar/avatarfullfacetracking"u8))
            return status with { AvatarSupport = value };
        if (address.SequenceEqual("/avatar/parameters/UserFaceTracking"u8))
            return status with { Face = value };
        if (address.SequenceEqual("/avatar/parameters/UserEyeTracking"u8))
            return status with { Eye = value };

        return status;
    }

    private static bool TryReadString(ref ReadOnlySpan<byte> packet, out ReadOnlySpan<byte> value)
    {
        int end = packet.IndexOf((byte)0);
        int paddedLength = (end + 4) & ~3;
        if (end < 0 || paddedLength > packet.Length)
        {
            value = default;
            return false;
        }

        value = packet[..end];
        packet = packet[paddedLength..];
        return true;
    }
}
