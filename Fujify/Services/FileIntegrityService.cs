/*
 * This file is part of Fujify.
 *
 * Copyright (C) 2024 Isidore Paulin contact@ipweb.dev
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Fujify.Services
{
    public class FileIntegrityService : IFileIntegrityService
    {
        public FileIntegrityResult VerifyIntegrity(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return FileIntegrityResult.Failure("File path is null or empty.");
            }

            if (!File.Exists(filePath))
            {
                return FileIntegrityResult.Failure($"File does not exist: '{filePath}'.");
            }

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0)
                {
                    return FileIntegrityResult.Failure("File size is 0 bytes.");
                }

                string ext = Path.GetExtension(filePath);
                if (ext.Equals(".dng", StringComparison.OrdinalIgnoreCase))
                {
                    return VerifyDng(filePath, fileInfo.Length);
                }
                else if (ext.Equals(".raf", StringComparison.OrdinalIgnoreCase))
                {
                    return VerifyRaf(filePath, fileInfo.Length);
                }

                // Generic integrity check for other raw / image formats
                return VerifyGeneric(filePath, fileInfo.Length);
            }
            catch (Exception ex)
            {
                return FileIntegrityResult.Failure($"I/O error during integrity verification: {ex.Message}");
            }
        }

        public Task<FileIntegrityResult> VerifyIntegrityAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return VerifyIntegrity(filePath);
            }, cancellationToken);
        }

        private static FileIntegrityResult VerifyDng(string filePath, long length)
        {
            if (length < 1024)
            {
                return FileIntegrityResult.Failure($"File size ({length} bytes) is too small to be a valid DNG.");
            }

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192);

            byte[] header = new byte[8];
            if (fs.Read(header, 0, 8) < 8)
            {
                return FileIntegrityResult.Failure("Unable to read TIFF/DNG header.");
            }

            bool isLittleEndian;
            if (header[0] == 0x49 && header[1] == 0x49)
            {
                isLittleEndian = true;
            }
            else if (header[0] == 0x4D && header[1] == 0x4D)
            {
                isLittleEndian = false;
            }
            else
            {
                return FileIntegrityResult.Failure("Invalid TIFF byte order mark (expected II or MM).");
            }

            ushort magic = isLittleEndian
                ? (ushort)(header[2] | (header[3] << 8))
                : (ushort)((header[2] << 8) | header[3]);

            if (magic != 42)
            {
                return FileIntegrityResult.Failure($"Invalid TIFF magic number (expected 42, got {magic}).");
            }

            uint ifdOffset = isLittleEndian
                ? (uint)(header[4] | (header[5] << 8) | (header[6] << 16) | (header[7] << 24))
                : (uint)((header[4] << 24) | (header[5] << 16) | (header[6] << 8) | header[7]);

            if (ifdOffset < 8 || ifdOffset >= length - 2)
            {
                return FileIntegrityResult.Failure($"IFD0 offset ({ifdOffset}) is out of file bounds.");
            }

            fs.Seek(ifdOffset, SeekOrigin.Begin);
            byte[] countBuf = new byte[2];
            if (fs.Read(countBuf, 0, 2) < 2)
            {
                return FileIntegrityResult.Failure("Could not read IFD0 entry count.");
            }

            ushort entryCount = isLittleEndian
                ? (ushort)(countBuf[0] | (countBuf[1] << 8))
                : (ushort)((countBuf[0] << 8) | countBuf[1]);

            if (entryCount == 0 || entryCount > 5000)
            {
                return FileIntegrityResult.Failure($"Invalid or unreasonable IFD0 entry count ({entryCount}).");
            }

            long ifdTableEnd = ifdOffset + 2 + (entryCount * 12);
            if (ifdTableEnd > length)
            {
                return FileIntegrityResult.Failure($"IFD0 table extends past end of file (table end: {ifdTableEnd}, file length: {length}).");
            }

            byte[] entriesBuf = new byte[entryCount * 12];
            if (fs.Read(entriesBuf, 0, entriesBuf.Length) < entriesBuf.Length)
            {
                return FileIntegrityResult.Failure("Incomplete IFD0 entries in file.");
            }

            bool hasDngVersion = false;
            bool hasUniqueModel = false;
            bool hasMake = false;
            bool hasModel = false;

            for (int i = 0; i < entryCount; i++)
            {
                int offset = i * 12;
                ushort tag = isLittleEndian
                    ? (ushort)(entriesBuf[offset] | (entriesBuf[offset + 1] << 8))
                    : (ushort)((entriesBuf[offset] << 8) | entriesBuf[offset + 1]);

                if (tag == 0xC612) hasDngVersion = true;
                if (tag == 0xC614) hasUniqueModel = true;
                if (tag == 0x010F) hasMake = true;
                if (tag == 0x0110) hasModel = true;
            }

            if (!hasDngVersion && !hasUniqueModel && !hasMake && !hasModel)
            {
                return FileIntegrityResult.Failure("DNG file lacks required camera/DNG metadata tags.");
            }

            // Verify trailing byte is readable (not truncated during write)
            fs.Seek(-1, SeekOrigin.End);
            if (fs.ReadByte() < 0)
            {
                return FileIntegrityResult.Failure("Failed to read trailing bytes; file may be truncated.");
            }

            return FileIntegrityResult.Success();
        }

        private static FileIntegrityResult VerifyRaf(string filePath, long length)
        {
            if (length < 148)
            {
                return FileIntegrityResult.Failure($"File size ({length} bytes) is too small to be a valid RAF header (minimum 148 bytes).");
            }

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096);
            byte[] header = new byte[148];
            if (fs.Read(header, 0, 148) < 148)
            {
                return FileIntegrityResult.Failure("Unable to read RAF header.");
            }

            // Check 16-byte magic
            byte[] magic = Encoding.ASCII.GetBytes("FUJIFILMCCD-RAW ");
            for (int i = 0; i < 16; i++)
            {
                if (header[i] != magic[i])
                {
                    return FileIntegrityResult.Failure("Not a valid Fujifilm RAF file (magic mismatch).");
                }
            }

            // Check camera model string
            string model = Encoding.ASCII.GetString(header, 0x1C, 32).Trim('\0', ' ');
            if (string.IsNullOrWhiteSpace(model))
            {
                return FileIntegrityResult.Failure("RAF header lacks valid camera model string.");
            }

            // Check meta_off at 0x5C and meta_len at 0x60
            uint metaOff = (uint)((header[0x5C] << 24) | (header[0x5D] << 16) | (header[0x5E] << 8) | header[0x5F]);
            uint metaLen = (uint)((header[0x60] << 24) | (header[0x61] << 16) | (header[0x62] << 8) | header[0x63]);

            if (metaOff == 0 || metaLen < 4 || metaOff + metaLen > length)
            {
                return FileIntegrityResult.Failure("Invalid CFA Meta Directory offset or length.");
            }

            // Verify CFA Directory can be read
            fs.Seek(metaOff, SeekOrigin.Begin);
            byte[] countBuf = new byte[4];
            if (fs.Read(countBuf, 0, 4) < 4)
            {
                return FileIntegrityResult.Failure("Unable to read CFA Meta Directory tag count.");
            }

            // Verify trailing byte
            fs.Seek(-1, SeekOrigin.End);
            if (fs.ReadByte() < 0)
            {
                return FileIntegrityResult.Failure("Failed to read trailing bytes; RAF file may be truncated.");
            }

            return FileIntegrityResult.Success();
        }

        private static FileIntegrityResult VerifyGeneric(string filePath, long length)
        {
            if (length < 64)
            {
                return FileIntegrityResult.Failure($"File size ({length} bytes) is too small to be a valid image file.");
            }

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096);
            byte[] startBuf = new byte[64];
            if (fs.Read(startBuf, 0, 64) < 64)
            {
                return FileIntegrityResult.Failure("Unable to read initial bytes of the file.");
            }

            fs.Seek(-1, SeekOrigin.End);
            if (fs.ReadByte() < 0)
            {
                return FileIntegrityResult.Failure("Unable to read trailing byte of the file.");
            }

            return FileIntegrityResult.Success();
        }
    }
}

