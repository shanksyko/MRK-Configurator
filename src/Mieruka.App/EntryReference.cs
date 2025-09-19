using System;
using System.Linq;
using System.Windows.Forms;

namespace Mieruka.App;

/// <summary>
/// Categorizes the types of entries that can be assigned to monitors.
/// </summary>
internal enum EntryKind
{
    /// <summary>
    /// Represents a native application entry.
    /// </summary>
    Application,

    /// <summary>
    /// Represents a site entry.
    /// </summary>
    Site,
}

/// <summary>
/// Identifies an application or site entry that can be dragged across the UI.
/// </summary>
internal sealed record class EntryReference
{
    private const string DataFormat = "Mieruka.App.EntryReference";

    private EntryReference(EntryKind kind, string id)
    {
        Kind = kind;
        Id = id;
    }

    /// <summary>
    /// Gets the kind of entry represented by the reference.
    /// </summary>
    public EntryKind Kind { get; }

    /// <summary>
    /// Gets the unique identifier associated with the entry.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Creates a new entry reference.
    /// </summary>
    /// <param name="kind">Entry category.</param>
    /// <param name="id">Unique identifier of the entry.</param>
    /// <returns>A new <see cref="EntryReference"/> instance.</returns>
    public static EntryReference Create(EntryKind kind, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Entry identifier cannot be empty.", nameof(id));
        }

        return new EntryReference(kind, id);
    }

    /// <summary>
    /// Creates a <see cref="DataObject"/> suitable for drag-and-drop operations.
    /// </summary>
    /// <param name="entry">Entry that should be serialized.</param>
    /// <returns>Data object containing the entry payload.</returns>
    public static DataObject CreateDataObject(EntryReference entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var data = new DataObject();
        data.SetData(DataFormat, false, entry);
        return data;
    }

    /// <summary>
    /// Attempts to materialize an entry reference from the provided <see cref="IDataObject"/>.
    /// </summary>
    /// <param name="dataObject">Drag-and-drop data source.</param>
    /// <param name="entry">Resulting entry reference, when available.</param>
    /// <returns><c>true</c> when the entry could be extracted; otherwise, <c>false</c>.</returns>
    public static bool TryGet(IDataObject? dataObject, out EntryReference? entry)
    {
        entry = null;

        if (dataObject is null)
        {
            return false;
        }

        if (dataObject.GetData(DataFormat, false) is EntryReference typed)
        {
            entry = typed;
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public bool Equals(EntryReference? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return Kind == other.Kind && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(Id));

    /// <summary>
    /// Searches for an entry in the provided <see cref="ListView"/>.
    /// </summary>
    /// <param name="listView">List view that contains the entry.</param>
    /// <param name="entry">Entry reference that should be located.</param>
    /// <returns>The <see cref="ListViewItem"/> when present; otherwise, <c>null</c>.</returns>
    public static ListViewItem? FindItem(ListView listView, EntryReference entry)
    {
        ArgumentNullException.ThrowIfNull(listView);
        ArgumentNullException.ThrowIfNull(entry);

        return listView.Items
            .Cast<ListViewItem>()
            .FirstOrDefault(item => item.Tag is EntryReference candidate && entry.Equals(candidate));
    }
}
