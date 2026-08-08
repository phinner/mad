using System.Collections.Immutable;
using NetCord.Rest;

namespace Mad.Discord;

public sealed class CommandMentions
{
    private ImmutableDictionary<string, ulong> _ids = ImmutableDictionary<string, ulong>.Empty;

    public void Replace(IEnumerable<ApplicationCommand> commands) =>
        _ids = commands.ToImmutableDictionary(command => command.Name, command => command.Id);

    public bool TryGetId(string commandName, out ulong id) => _ids.TryGetValue(commandName, out id);
}
