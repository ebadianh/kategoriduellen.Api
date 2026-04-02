namespace kategoriduellen.Api.Models;

public record SubmitAnswersRequest(Guid PlayerId, Dictionary<string, string> Answers);
