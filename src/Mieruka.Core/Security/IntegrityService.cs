using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Mieruka.Core.Security;

/// <summary>
/// Validates file integrity using SHA-256 hashes.
/// </summary>
public sealed class IntegrityService
{
    private readonly AuditLog? _auditLog;

    /// <summary>
    /// Initializes a new instance of the <see cref="IntegrityService"/> class.
    /// </summary>
    public IntegrityService(AuditLog? auditLog = null)
    {
        _auditLog = auditLog;
    }

    /// <summary>
    /// Validates the supplied manifest.
    /// </summary>
    /// <param name="manifest">Integrity manifest.</param>
    public void Validate(IntegrityManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        foreach (var file in manifest.Files)
        {
            ValidateEntry(file);
        }
    }

    private void ValidateEntry(FileIntegrityExpectation expectation)
    {
        if (!File.Exists(expectation.Path))
        {
            _auditLog?.RecordIntegrityFailure(expectation.Path, expectation.ExpectedHash, "missing");
            throw new IntegrityViolationException($"Integrity check failed for '{expectation.Path}'. File is missing.");
        }

        using var stream = File.OpenRead(expectation.Path);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        var actual = Convert.ToHexString(hash);
        if (!string.Equals(actual, expectation.ExpectedHash, StringComparison.OrdinalIgnoreCase))
        {
            _auditLog?.RecordIntegrityFailure(expectation.Path, expectation.ExpectedHash, actual);
            throw new IntegrityViolationException($"Integrity check failed for '{expectation.Path}'. Expected {expectation.ExpectedHash} but found {actual}.");
        }
    }
}

/// <summary>
/// Describes the expected integrity of files.
/// </summary>
public sealed record class IntegrityManifest(int Version, IReadOnlyList<FileIntegrityExpectation> Files)
{
    /// <summary>
    /// Allows the manifest to be updated when an override is approved.
    /// </summary>
    public IntegrityManifest WithOverride(string path, string newHash)
    {
        var updated = Files.Select(file => file.Path.Equals(path, StringComparison.OrdinalIgnoreCase)
            ? file with { ExpectedHash = newHash }
            : file).ToList();
        return new IntegrityManifest(Version + 1, updated);
    }
}

/// <summary>
/// Describes an expected file hash.
/// </summary>
public sealed record class FileIntegrityExpectation(string Path, string ExpectedHash);

/// <summary>
/// Exception thrown when integrity validation fails.
/// </summary>
public sealed class IntegrityViolationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IntegrityViolationException"/> class.
    /// </summary>
    public IntegrityViolationException(string message)
        : base(message)
    {
    }
}
