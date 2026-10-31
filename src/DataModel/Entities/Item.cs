// <copyright file="Item.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

using System.Globalization;
using MUnique.OpenMU.Annotations;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.DataModel.Properties;

/// <summary>
/// The item.
/// </summary>
[Cloneable]
public partial class Item
{
    /// <summary>
    /// Gets or sets the item slot in the <see cref="ItemStorage"/>.
    /// </summary>
    public byte ItemSlot { get; set; }

    /// <summary>
    /// Gets or sets the item definition.
    /// </summary>
    [Required]
    public virtual ItemDefinition? Definition { get; set; }

    /// <summary>
    /// Gets or sets the currently remaining durability.
    /// </summary>
    public double Durability { get; set; }

    /// <summary>
    /// Gets or sets the level of the item.
    /// </summary>
    public byte Level { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this item instance provides the weapon skill while being equipped.
    /// </summary>
    public bool HasSkill { get; set; }

    /// <summary>
    /// Gets or sets the item options.
    /// </summary>
    [MemberOfAggregate]
    public virtual ICollection<ItemOptionLink> ItemOptions { get; protected set; } = null!;

    /// <summary>
    /// Gets or sets the applied item set groups (Ancient Set).
    /// </summary>
    public virtual ICollection<ItemOfItemSet> ItemSetGroups { get; protected set; } = null!;

    /// <summary>
    /// Gets or sets the socket count. This limits the amount of socket options in the <see cref="ItemOptions"/>.
    /// </summary>
    public int SocketCount { get; set; }

    /// <summary>
    /// Gets or sets the price which was set by the player for his personal store.
    /// </summary>
    public int? StorePrice { get; set; }

    /// <summary>
    /// Gets or sets the pet experience.
    /// Only applies, if this item is actually a trainable pet.
    /// </summary>
    public int PetExperience { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when this item instance was activated, i.e. when its
    /// contracted duration started counting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only items with a contracted duration (Cash Shop / XShop temporary items) carry these
    /// timestamps. The duration starts counting <b>only</b> when the item is claimed from the
    /// Cash Shop storage and delivered to the character inventory — never on purchase, gift
    /// receipt, storage listing, login or equip.
    /// </para>
    /// <para>
    /// <c>null</c> means "no expiration information": legacy items which were created before this
    /// feature existed, and permanent items, are both represented this way and must never be
    /// considered expired. The persisted timestamps are the source of truth, so the expiration
    /// keeps counting across logout, server restart and container recreation.
    /// </para>
    /// <para>
    /// <see cref="ExpiresAtUtc"/> is always <see cref="ActivatedAtUtc"/> plus the contracted
    /// duration in seconds. The two values are written together, in the same save, and are never
    /// renewed by trades, storage transfers or stack merges.
    /// </para>
    /// </remarks>
    public DateTime? ActivatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when this item instance expires.
    /// </summary>
    /// <remarks>
    /// A value less than or equal to the current UTC time means the item is expired and must not be
    /// usable, equippable, tradable or able to grant attributes or effects. <c>null</c> means the
    /// item never expires (legacy or permanent item) — see <see cref="ActivatedAtUtc"/>.
    /// </remarks>
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>
    /// Assigns the values of another item to this item.
    /// </summary>
    /// <param name="otherItem">The other item.</param>
    public void AssignValues(Item otherItem)
    {
        this.Definition = otherItem.Definition;
        this.Durability = otherItem.Durability;
        this.Level = otherItem.Level;
        this.HasSkill = otherItem.HasSkill;
        this.SocketCount = otherItem.SocketCount;
        this.PetExperience = otherItem.PetExperience;
        if (otherItem.ItemOptions != null && otherItem.ItemOptions.Any())
        {
            this.ItemOptions.Clear();
            foreach (var option in otherItem.ItemOptions)
            {
                this.ItemOptions.Add(this.CloneItemOptionLink(option));
            }
        }

        if (otherItem.ItemSetGroups != null && otherItem.ItemSetGroups.Any())
        {
            this.ItemSetGroups.Clear();
            foreach (var setGroup in otherItem.ItemSetGroups)
            {
                this.ItemSetGroups.Add(setGroup);
            }
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.Append("Slot ").Append(this.ItemSlot).Append(": ");

        if (this.ItemOptions.Any(o => o.ItemOption?.OptionType == ItemOptionTypes.Excellent))
        {
            stringBuilder.Append("Excellent ");
        }

        var ancientSet = this.ItemSetGroups.FirstOrDefault(s => s.AncientSetDiscriminator != 0)?.ItemSetGroup;
        if (ancientSet != null)
        {
            stringBuilder.Append(ancientSet.Name).Append(" ");
        }

        var itemName = this.Definition?.GetNameForLevel(this.Level);

        stringBuilder.Append(itemName);
        if (this.Level > 0)
        {
            stringBuilder.Append("+").Append(this.Level);
        }

        foreach (var option in this.ItemOptions
                     .Where(o => o.ItemOption?.OptionType != ItemOptionTypes.Luck)
                     .OrderBy(o => o.ItemOption?.OptionType == ItemOptionTypes.Option))
        {
            var levelOption = option.ItemOption?.LevelDependentOptions.FirstOrDefault(o => o.Level == (option.ItemOption.LevelType == LevelType.ItemLevel ? this.Level : option.Level));
            var powerUpDefinition = levelOption?.PowerUpDefinition ?? option.ItemOption?.PowerUpDefinition;
            if (powerUpDefinition is not null)
            {
                stringBuilder.Append("+").Append(powerUpDefinition);
            }
        }

        if (this.HasSkill)
        {
            stringBuilder.Append("+Skill");
        }

        if (this.ItemOptions.Any(opt => opt.ItemOption?.OptionType == ItemOptionTypes.Luck))
        {
            stringBuilder.Append("+Luck");
        }

        if (this.SocketCount > 0)
        {
            stringBuilder.Append("+").Append(this.SocketCount).Append("S");
        }

        return stringBuilder.ToString();
    }

    /// <inheritdoc/>
    public string ToString(CultureInfo culture)
    {
        using var cultureHelper = CultureHelper.SetTemporaryCulture(culture);
        var stringBuilder = new StringBuilder();
        stringBuilder.Append(Resources.Slot).Append(" ").Append(this.ItemSlot).Append(": ");

        if (this.ItemOptions.Any(o => o.ItemOption?.OptionType == ItemOptionTypes.Excellent))
        {
            stringBuilder.Append(Resources.Excellent).Append(" ");
        }

        var ancientSet = this.ItemSetGroups.FirstOrDefault(s => s.AncientSetDiscriminator != 0)?.ItemSetGroup;
        if (ancientSet != null)
        {
            stringBuilder.Append(ancientSet.Name.ToString()).Append(" ");
        }

        var itemName = this.Definition?.GetNameForLevel(this.Level);

        stringBuilder.Append(itemName);
        if (this.Level > 0)
        {
            stringBuilder.Append("+").Append(this.Level);
        }

        foreach (var option in this.ItemOptions
                     .Where(o => o.ItemOption?.OptionType != ItemOptionTypes.Luck)
                     .OrderBy(o => o.ItemOption?.OptionType == ItemOptionTypes.Option))
        {
            var levelOption = option.ItemOption?.LevelDependentOptions.FirstOrDefault(o => o.Level == (option.ItemOption.LevelType == LevelType.ItemLevel ? this.Level : option.Level));
            var powerUpDefinition = levelOption?.PowerUpDefinition ?? option.ItemOption?.PowerUpDefinition;
            if (powerUpDefinition is not null)
            {
                stringBuilder.Append("+").Append(powerUpDefinition);
            }
        }

        if (this.HasSkill)
        {
            stringBuilder.Append("+").Append(Resources.Skill);
        }

        if (this.ItemOptions.Any(opt => opt.ItemOption?.OptionType == ItemOptionTypes.Luck))
        {
            stringBuilder.Append("+").Append(Resources.Luck);
        }

        if (this.SocketCount > 0)
        {
            stringBuilder.Append("+").Append(this.SocketCount).Append(Resources.SocketAbbreviation);
        }

        return stringBuilder.ToString();
    }

    /// <summary>
    /// Clones the item option link.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <returns>The cloned item option link.</returns>
    protected virtual ItemOptionLink CloneItemOptionLink(ItemOptionLink link)
    {
        return link.Clone();
    }
}