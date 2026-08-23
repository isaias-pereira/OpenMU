using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

namespace MUnique.OpenMU.StatAliases
{
    [Guid("0F000001-1111-4111-8111-111111111111")]
    [PlugIn]
    [ChatCommandHelp("/f", typeof(AddStatChatCommandPlugIn.Arguments), (CharacterStatus)0)]
    public class AddStrengthAliasPlugIn : AddStrengthStatChatCommandPlugIn { }

    [Guid("0F000002-2222-4222-8222-222222222222")]
    [PlugIn]
    [ChatCommandHelp("/a", typeof(AddStatChatCommandPlugIn.Arguments), (CharacterStatus)0)]
    public class AddAgilityAliasPlugIn : AddAgilityStatChatCommandPlugIn { }

    [Guid("0F000003-3333-4333-8333-333333333333")]
    [PlugIn]
    [ChatCommandHelp("/v", typeof(AddStatChatCommandPlugIn.Arguments), (CharacterStatus)0)]
    public class AddVitalityAliasPlugIn : AddVitalityStatChatCommandPlugIn { }

    [Guid("0F000004-4444-4444-8444-444444444444")]
    [PlugIn]
    [ChatCommandHelp("/e", typeof(AddStatChatCommandPlugIn.Arguments), (CharacterStatus)0)]
    public class AddEnergyAliasPlugIn : AddEnergyStatChatCommandPlugIn { }

    [Guid("0F000005-5555-4555-8555-555555555555")]
    [PlugIn]
    [ChatCommandHelp("/c", typeof(AddStatChatCommandPlugIn.Arguments), (CharacterStatus)0)]
    public class AddCommandAliasPlugIn : AddCommandStatChatCommandPlugIn { }
}