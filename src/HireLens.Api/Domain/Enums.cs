namespace HireLens.Api.Domain;

public enum JobStatus { Open, Closed }

public enum ApplicationStatus { Applied, Shortlisted, Rejected }

/// <summary>Whether NER has processed the resume. "Pending" means not yet, or the NER call failed and can be retried.</summary>
public enum NerStatus { Pending, Done }
