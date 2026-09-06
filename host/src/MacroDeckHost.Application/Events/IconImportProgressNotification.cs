using MacroDeckHost.Domain.Entities;
using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record IconImportProgressNotification(IconImportBatchEntity Batch, int? Total, int Processed, int Failed)
	: INotification;
