// <copyright file="NewPlayersInScopeExtendedPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.GameLogic.Views.World;
using MUnique.OpenMU.GameServer.RemoteView.Character;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using MUnique.OpenMU.Network.PlugIns;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// The extended implementation of the <see cref="INewPlayersInScopePlugIn"/> which is forwarding everything to the game client with specific data packets.
/// </summary>
[PlugIn]
[Display(Name = nameof(PlugInResources.NewPlayersInScopeExtendedPlugIn_Name), Description = nameof(PlugInResources.NewPlayersInScopeExtendedPlugIn_Description), ResourceType = typeof(PlugInResources))]
[Guid("83E30752-501E-4A40-9698-9A5097825C30")]
[MinimumClient(106, 3, ClientLanguage.Invariant)]
public class NewPlayersInScopeExtendedPlugIn : NewPlayersInScopePlugIn, INewPlayersInScopePlugIn
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NewPlayersInScopeExtendedPlugIn"/> class.
    /// </summary>
    /// <param name="player">The player.</param>
    public NewPlayersInScopeExtendedPlugIn(RemotePlayer player)
        : base(player)
    {
    }

    /// <inheritdoc />
    protected override async ValueTask SendCharacterAsync(Player newPlayer, bool isSpawned)
    {
        var connection = this.Player.Connection;
        if (connection is null)
        {
            return;
        }

        var selectedCharacter = newPlayer.SelectedCharacter;
        if (selectedCharacter is null)
        {
            return;
        }

        int Write()
        {
            var appearanceSerializer = this.Player.AppearanceSerializer;
            Span<byte> activeEffects = stackalloc byte[newPlayer.MagicEffectList.VisibleEffects.Count];
            for (int i = 0; i < activeEffects.Length && i < newPlayer.MagicEffectList.VisibleEffects.Count; i++)
            {
                activeEffects[i] = (byte)newPlayer.MagicEffectList.VisibleEffects[i].Id;
            }

            var requiredSize = AddCharacterToScopeExtendedRef.GetRequiredSize(appearanceSerializer.NeededSpace + activeEffects.Length + 1);

            var span = connection.Output.GetSpan(requiredSize)[..requiredSize];
            var packet = new AddCharacterToScopeExtendedRef(span);

            packet.Id = newPlayer.GetId(this.Player);
            if (isSpawned)
            {
                packet.Id |= 0x8000;
            }

            packet.CurrentPositionX = newPlayer.Position.X;
            packet.CurrentPositionY = newPlayer.Position.Y;

            packet.Name = selectedCharacter.Name;
            if (newPlayer.IsWalking)
            {
                packet.TargetPositionX = newPlayer.WalkTarget.X;
                packet.TargetPositionY = newPlayer.WalkTarget.Y;
            }
            else
            {
                packet.TargetPositionX = newPlayer.Position.X;
                packet.TargetPositionY = newPlayer.Position.Y;
            }

            packet.Rotation = newPlayer.Rotation.ToPacketByte();
            packet.HeroState = selectedCharacter.State.Convert();
            packet.AttackSpeed = (ushort)(newPlayer.Attributes?[Stats.AttackSpeed] ?? 0);
            packet.MagicSpeed = (ushort)(newPlayer.Attributes?[Stats.MagicSpeed] ?? 0);

            appearanceSerializer.WriteAppearanceData(packet.AppearanceAndEffects, newPlayer.AppearanceData, true);

            var effectsStartIndex = appearanceSerializer.NeededSpace;
            packet.AppearanceAndEffects[effectsStartIndex] = (byte)activeEffects.Length;
            for (int e = 0; e < activeEffects.Length; ++e)
            {
                packet.AppearanceAndEffects[effectsStartIndex + 1 + e] = activeEffects[e];
            }

            return span.Length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async ValueTask SendTransformedCharacterAsync(Player newPlayer, bool isSpawned)
    {
        // RC2/GATE B: the base implementation sends the classic transformed packet whose
        // appearance block is 18 bytes — too small for this serializer (27 bytes), which threw
        // "Target span too small. Actual size: 18; Required: 27" on every transformed player in
        // scope (production stacks of RC1, 25/26-10). The Season 6 Episode 3 client parses C2 0x45
        // as PCREATE_TRANSFORM_EXTENDED (WSclient.cpp ReceiveCreateTransformViewport): the same
        // field order as the classic packet, but with a 27-byte appearance block (its Class+Flags
        // bytes plus Equipment[25]) — which is exactly what the extended serializer writes.
        var connection = this.Player.Connection;
        if (connection is null)
        {
            return;
        }

        var selectedCharacter = newPlayer.SelectedCharacter;
        if (selectedCharacter is null)
        {
            return;
        }

        int Write()
        {
            var appearanceSerializer = this.Player.AppearanceSerializer;
            var activeEffects = newPlayer.MagicEffectList.VisibleEffects;
            const int estimatedEffectsPerPlayer = 5;
            var estimatedSizePerCharacter = AddTransformedCharactersToScopeExtendedRef.CharacterDataRef.GetRequiredSize(Math.Max(estimatedEffectsPerPlayer, activeEffects.Count));
            var estimatedSize = AddTransformedCharactersToScopeExtendedRef.GetRequiredSize(1, estimatedSizePerCharacter);
            var span = connection.Output.GetSpan(estimatedSize)[..estimatedSize];
            var packet = new AddTransformedCharactersToScopeExtendedRef(span)
            {
                CharacterCount = 1,
            };

            var playerBlock = packet[0];
            playerBlock.Id = newPlayer.GetId(this.Player);
            if (isSpawned)
            {
                playerBlock.Id |= 0x8000;
            }

            playerBlock.CurrentPositionX = newPlayer.Position.X;
            playerBlock.CurrentPositionY = newPlayer.Position.Y;

            appearanceSerializer.WriteAppearanceData(playerBlock.Appearance, newPlayer.AppearanceData, true);
            playerBlock.Name = selectedCharacter.Name;
            if (newPlayer.IsWalking)
            {
                playerBlock.TargetPositionX = newPlayer.WalkTarget.X;
                playerBlock.TargetPositionY = newPlayer.WalkTarget.Y;
            }
            else
            {
                playerBlock.TargetPositionX = newPlayer.Position.X;
                playerBlock.TargetPositionY = newPlayer.Position.Y;
            }

            playerBlock.Rotation = newPlayer.Rotation.ToPacketByte();
            playerBlock.HeroState = selectedCharacter.State.Convert();

            playerBlock.EffectCount = (byte)activeEffects.Count;
            playerBlock.Skin = (ushort)newPlayer.Attributes![Stats.TransformationSkin];
            for (int e = playerBlock.EffectCount - 1; e >= 0; e--)
            {
                var effectBlock = playerBlock[e];
                effectBlock.Id = (byte)activeEffects[e].Id;
            }

            // The calculation of the final size is not a requirement, but we do it to save some traffic.
            // The original server also doesn't send more bytes than necessary.
            var finalSize = packet.FinalSize;
            span.Slice(0, finalSize).SetPacketSize();
            return finalSize;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }
}