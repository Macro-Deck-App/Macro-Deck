using MacroDeckHost.Domain.Entities;
using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record VariableValueChangedNotification(
	VariableEntity Variable,
	string PreviousValue) : INotification;
