namespace AssistantCore.Service.Application.Models.Messages.Tools;

public static class ToolExecutionErrorCodes
{
    public const string ExecutorNotFound = "TOOL_EXECUTOR_NOT_FOUND";

    public const string ExecutorAmbiguous = "TOOL_EXECUTOR_AMBIGUOUS";

    public const string ArgumentMappingFailed = "TOOL_ARGUMENT_MAPPING_FAILED";

    public const string EnterpriseSearchUnavailable = "ENTERPRISE_SEARCH_UNAVAILABLE";

    public const string EnterpriseSearchTimeout = "ENTERPRISE_SEARCH_TIMEOUT";

    public const string OutlookMailboxUnavailable = "OUTLOOK_MAILBOX_UNAVAILABLE";

    public const string OutlookMailboxTimeout = "OUTLOOK_MAILBOX_TIMEOUT";

    public const string SpreadsheetAnalysisFailed = "SPREADSHEET_ANALYSIS_FAILED";

    public const string SpreadsheetAnalysisTimeout = "SPREADSHEET_ANALYSIS_TIMEOUT";
}
