namespace Viva.Chat.Contracts;

public sealed record ChatMessage
(
  string Id,
  string SiteId,
  string Room,
  string User,
  string Text,
  DateTimeOffset SentAt
);
