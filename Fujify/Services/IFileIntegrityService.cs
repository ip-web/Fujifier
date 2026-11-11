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

using System.Threading;
using System.Threading.Tasks;

namespace Fujify.Services
{
    public class FileIntegrityResult
    {
        public bool IsValid { get; }
        public string? ErrorMessage { get; }

        public FileIntegrityResult(bool isValid, string? errorMessage = null)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
        }

        public static FileIntegrityResult Success() => new(true);
        public static FileIntegrityResult Failure(string errorMessage) => new(false, errorMessage);
    }

    public interface IFileIntegrityService
    {
        FileIntegrityResult VerifyIntegrity(string filePath);
        Task<FileIntegrityResult> VerifyIntegrityAsync(string filePath, CancellationToken cancellationToken = default);
    }
}

