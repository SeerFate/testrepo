namespace SmsIrCheck;

internal static class StatusText
{
    public static string Describe(int status) => status switch
    {
        1 => "Success",
        0 => "Problem on the sms.ir side",
        10 => "API key is invalid",
        11 => "API key is disabled",
        12 => "API key is limited to specific IP addresses",
        13 => "User account is disabled",
        14 => "User account is suspended",
        20 => "Too many requests",
        101 => "Sender line number is invalid",
        102 => "Not enough credit",
        103 => "Message text is empty",
        104 => "One or more mobile numbers are invalid",
        105 => "More than 100 mobile numbers",
        106 => "More than 100 message texts",
        107 => "Mobile list is empty",
        108 => "Text list is empty",
        109 => "Send time is invalid",
        110 => "Mobile count and text count do not match",
        111 => "No send was registered with this id",
        112 => "Nothing found to delete",
        113 => "Template was not found",
        114 => "A parameter value is longer than 25 characters",
        115 => "Mobile number is on the sms.ir blacklist",
        116 => "Parameter name cannot be empty",
        117 => "Message text was not approved",
        118 => "Too many messages",
        119 => "Custom templates require a higher plan",
        123 => "Sender line needs activation",
        _ => "Undocumented status code",
    };

    public static string DescribeDelivery(int? state) => state switch
    {
        null => "No delivery state yet",
        1 => "Delivered to the handset",
        2 => "Not delivered to the handset",
        3 => "Processing at the operator",
        4 => "Not delivered to the operator",
        5 => "Delivered to the operator",
        6 => "Error",
        7 => "Blacklist",
        _ => "Undocumented delivery state",
    };

    public static string DescribeHttp(int? status) => status switch
    {
        200 => "The call was accepted at the HTTP layer. Read the JSON status field for the real result.",
        400 => "sms.ir rejected the request as invalid.",
        401 => "Authentication failed.",
        405 => "This URL does not allow that HTTP method.",
        429 => "Too many requests.",
        500 => "Unexpected error on sms.ir.",
        null => "No HTTP response.",
        _ => "HTTP status from sms.ir.",
    };
}
