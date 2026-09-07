// <copyright file="PostChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A chat command plugin which allows a player to post a message as global blue system message.
/// </summary>
[Guid("ED2523C1-F66D-4B53-814E-D2FC0C1F46C0")]
[PlugIn]
[Display(Name = nameof(PlugInResources.PostChatCommandPlugIn_Name), Description = nameof(PlugInResources.PostChatCommandPlugIn_Description), ResourceType = typeof(PlugInResources))]
[ChatCommandHelp(Command, null, MinimumStatus)]
public class PostChatCommandPlugIn : IChatCommandPlugIn, ISupportCustomConfiguration<PostChatCommandPlugIn.PostConfiguration>, ISupportDefaultCustomConfiguration
{
    private const string Command = "/post";

    private const CharacterStatus MinimumStatus = CharacterStatus.Normal;

    /// <summary>
    /// The timestamps of the last posts, by character id. It's static, so that all commands which
    /// derive from this plugin (e.g. the short <c>/p</c> variant) share the same cooldown and the
    /// players can't bypass it by alternating between the command keys.
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, DateTime> LastPostTimestamps = new();

    /// <summary>
    /// Gets or sets the configuration.
    /// </summary>
    public PostConfiguration? Configuration { get; set; }

    /// <inheritdoc />
    public virtual string Key => Command;

    /// <inheritdoc />
    public virtual CharacterStatus MinCharacterStatusRequirement => MinimumStatus;

    /// <inheritdoc />
    public object CreateDefaultConfig() => new PostConfiguration();

    /// <inheritdoc />
    public virtual async ValueTask HandleCommandAsync(Player player, string command)
    {
        if (player.SelectedCharacter is not { } selectedCharacter)
        {
            return;
        }

        var message = command.Length > this.Key.Length ? command[this.Key.Length..].Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var configuration = this.Configuration ??= (PostConfiguration)this.CreateDefaultConfig();

        if (await IsChatBannedAsync(player).ConfigureAwait(false))
        {
            return;
        }

        // Game masters have to be able to announce longer texts and to answer immediately,
        // so the restrictions only apply to normal players.
        if (selectedCharacter.CharacterStatus < CharacterStatus.GameMaster)
        {
            if (message.Length > configuration.MaximumMessageLength)
            {
                await ShowMessageAsync(player, configuration.MessageTooLongMessage, configuration.MaximumMessageLength).ConfigureAwait(false);
                return;
            }

            if (!TryStartCooldown(selectedCharacter.Id, configuration.CooldownSeconds, out var remainingSeconds))
            {
                await ShowMessageAsync(player, configuration.CooldownMessage, remainingSeconds).ConfigureAwait(false);
                return;
            }
        }

        var messageFormat = configuration.MessageFormat.GetTranslation(player.Culture) is { Length: > 0 } configuredFormat
            ? configuredFormat
            : PostConfiguration.DefaultMessageFormat;
        if (!TryFormat(messageFormat, out var postMessage, selectedCharacter.Name, message))
        {
            // The configured format is invalid, so we post with the default format instead of not posting at all.
            postMessage = string.Format(PostConfiguration.DefaultMessageFormat, selectedCharacter.Name, message);
        }

        await player.GameContext.SendGlobalMessageAsync(postMessage, MessageType.BlueNormal).ConfigureAwait(false);
    }

    /// <summary>
    /// Formats the given format string. Without this guard, a misconfigured message format would
    /// throw a <see cref="FormatException"/> for every player who uses the command.
    /// </summary>
    /// <param name="format">The format string.</param>
    /// <param name="result">The formatted string, or the unchanged <paramref name="format"/> if it's invalid.</param>
    /// <param name="formatArguments">The format arguments.</param>
    /// <returns><see langword="true"/>, if the format string was valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryFormat(string format, out string result, params object?[] formatArguments)
    {
        try
        {
            result = string.Format(format, formatArguments);
            return true;
        }
        catch (FormatException)
        {
            result = format;
            return false;
        }
    }

    private static async ValueTask ShowMessageAsync(Player player, LocalizedString message, params object?[] formatArguments)
    {
        if (message.GetTranslation(player.Culture) is { Length: > 0 } translation)
        {
            TryFormat(translation, out var text, formatArguments);
            await player.ShowBlueMessageAsync(text).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Determines whether the character may post again and starts the next cooldown if it may.
    /// </summary>
    /// <param name="characterId">The id of the posting character.</param>
    /// <param name="cooldownSeconds">The configured cooldown in seconds.</param>
    /// <param name="remainingSeconds">The remaining cooldown in seconds, when the character has to wait.</param>
    /// <returns><see langword="true"/>, if the character may post; otherwise, <see langword="false"/>.</returns>
    private static bool TryStartCooldown(Guid characterId, int cooldownSeconds, out int remainingSeconds)
    {
        remainingSeconds = 0;
        if (cooldownSeconds <= 0)
        {
            return true;
        }

        var now = DateTime.UtcNow;
        var cooldown = TimeSpan.FromSeconds(cooldownSeconds);
        var nextAllowedPost = now;

        // The update function of AddOrUpdate is called under a lock for the key, so only one of
        // multiple concurrent posts of the same character can win the race for the cooldown.
        LastPostTimestamps.AddOrUpdate(
            characterId,
            now,
            (_, lastPost) =>
            {
                if (now - lastPost < cooldown)
                {
                    nextAllowedPost = lastPost + cooldown;
                    return lastPost;
                }

                return now;
            });

        if (nextAllowedPost <= now)
        {
            return true;
        }

        remainingSeconds = Math.Max(1, (int)Math.Ceiling((nextAllowedPost - now).TotalSeconds));
        return false;
    }

    private static async ValueTask<bool> IsChatBannedAsync(Player player)
    {
        var remainingChatBan = (player.Account?.ChatBanUntil ?? default) - DateTime.UtcNow;
        if (remainingChatBan <= TimeSpan.Zero)
        {
            return false;
        }

        if (remainingChatBan.TotalMinutes >= 1)
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.ChatBanMinutesRemaining), (int)Math.Ceiling(remainingChatBan.TotalMinutes)).ConfigureAwait(false);
        }
        else
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.ChatBanSecondsRemaining), (int)Math.Ceiling(remainingChatBan.TotalSeconds)).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// The configuration of a <see cref="PostChatCommandPlugIn"/>.
    /// </summary>
    public class PostConfiguration
    {
        /// <summary>
        /// The message format which is used when none is configured.
        /// </summary>
        internal const string DefaultMessageFormat = "[POST] {0}: {1}";

        /// <summary>
        /// Gets or sets the time in seconds a player has to wait between two posts. Game masters are not affected.
        /// </summary>
        [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.PostConfiguration_CooldownSeconds_Name), Description = nameof(PlugInResources.PostConfiguration_CooldownSeconds_Description))]
        public int CooldownSeconds { get; set; } = 30;

        /// <summary>
        /// Gets or sets the maximum number of characters of the message itself, without the sender name.
        /// </summary>
        [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.PostConfiguration_MaximumMessageLength_Name), Description = nameof(PlugInResources.PostConfiguration_MaximumMessageLength_Description))]
        public int MaximumMessageLength { get; set; } = 60;

        /// <summary>
        /// Gets or sets the format of the global message, where {0} is the character name and {1} is the message.
        /// </summary>
        [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.PostConfiguration_MessageFormat_Name), Description = nameof(PlugInResources.PostConfiguration_MessageFormat_Description))]
        public LocalizedString MessageFormat { get; set; } = DefaultMessageFormat;

        /// <summary>
        /// Gets or sets the message which is shown to the player when the cooldown is still running, where {0} is the remaining time in seconds.
        /// </summary>
        [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.PostConfiguration_CooldownMessage_Name), Description = nameof(PlugInResources.PostConfiguration_CooldownMessage_Description))]
        public LocalizedString CooldownMessage { get; set; } = "You have to wait {0} seconds until your next post.";

        /// <summary>
        /// Gets or sets the message which is shown to the player when the message is too long, where {0} is the maximum length.
        /// </summary>
        [Display(ResourceType = typeof(PlugInResources), Name = nameof(PlugInResources.PostConfiguration_MessageTooLongMessage_Name), Description = nameof(PlugInResources.PostConfiguration_MessageTooLongMessage_Description))]
        public LocalizedString MessageTooLongMessage { get; set; } = "Your post is too long. At most {0} characters are allowed.";
    }
}
