# sms.ir API analysis

This is the contract the connectivity app calls. It comes from three sources that describe the same service:

- The developer guide at `https://app.sms.ir/developer/help/introduction`. Opened without a login, that address redirects to the SMS.ir sign-in page, so the in-panel copy could not be read directly.
- The public REST guide at `https://sms.ir/rest-api/`, which contains the same introduction: headers, HTTP status codes, the response envelope, Unix time, and every send method.
- The live OpenAPI document at `https://api.sms.ir/swagger/v1/swagger.json` (title: `sms.ir لیست وب سرویس های سامانه پیامک`, version `v1`).

Calls below were also checked against the live host on 30 Sep 2026.

## Base URL and authentication

| Item | Value |
| --- | --- |
| Host | `https://api.sms.ir` |
| Prefix | `/v1` |
| Auth header | `x-api-key` (the guide also writes `X-API-KEY`; HTTP header names are case-insensitive) |
| Body | `Content-Type: application/json; charset=utf-8` |
| Accept | `application/json` |
| Key location | Developer panel: برنامه‌نویسان → API keys |

The key is sent on every request. A missing or wrong key does not close the connection. The live host answers:

```json
{"data":null,"status":10,"message":"کلید وب سرویس نامعتبر است"}
```

with HTTP `401`.

There is no separate sandbox host. A sandbox key and a production key use the same URLs. The key type decides whether a send is simulated or real. Sandbox keys are created in the same API-key screen. Sandbox sends do not deliver a handset message, do not spend credit, and do not show up in reports.

## Response envelope

Every JSON body has the same shape:

| Field | Meaning |
| --- | --- |
| `status` | Business result. `1` means success. This is not the HTTP status. |
| `message` | Short description, usually Persian (`موفق` on success). |
| `data` | Payload, or `null` on failure. |

HTTP status and `status` are different checks:

| HTTP | Meaning |
| --- | --- |
| 200 | The HTTP call was processed. Still read `status`. |
| 400 | The request was rejected as invalid. The body still carries `status`. |
| 401 | Authentication failed. Live body uses `status` 10. |
| 429 | Too many requests. |
| 500 | Unexpected server error. |

Times in the API are Unix seconds in UTC (`sendDateTime`, `deliveryDateTime`, report filters).

## Business status codes

| Code | Meaning |
| --- | --- |
| 1 | Success |
| 0 | Problem on the sms.ir side |
| 10 | API key is invalid |
| 11 | API key is disabled |
| 12 | API key is limited to specific IP addresses |
| 13 | User account is disabled |
| 14 | User account is suspended |
| 20 | Too many requests |
| 101 | Sender line number is invalid |
| 102 | Not enough credit |
| 103 | Message text is empty |
| 104 | One or more mobile numbers are invalid |
| 105 | More than 100 mobile numbers |
| 106 | More than 100 message texts |
| 107 | Mobile list is empty |
| 108 | Text list is empty |
| 109 | Send time is invalid |
| 110 | Mobile count and text count do not match |
| 111 | No send was registered with this id |
| 112 | Nothing found to delete |
| 113 | Template was not found |
| 114 | A parameter value is longer than 25 characters |
| 115 | Mobile number is on the sms.ir blacklist |
| 116 | Parameter name cannot be empty |
| 117 | Message text was not approved |
| 118 | Too many messages |
| 119 | Custom templates require a higher plan |
| 123 | Sender line needs activation |

Delivery state on a sent message:

| Code | Meaning |
| --- | --- |
| 1 | Delivered to the handset |
| 2 | Not delivered to the handset |
| 3 | Processing at the operator |
| 4 | Not delivered to the operator |
| 5 | Delivered to the operator |
| 6 | Error |
| 7 | Blacklist |

On a bulk send, each entry in `messageIds` lines up with the mobile you sent:

- a positive id: the message was queued
- `0`: that number is blacklisted
- `null`: the number is invalid, or the text is too long

## Endpoints this app uses

### Credit

`GET /v1/credit`

Proves DNS, TLS, and the API key without sending an SMS.

```json
{"status":1,"message":"موفق","data":165.3}
```

`data` is the remaining credit.

### Sender lines

`GET /v1/line`

```json
{"status":1,"message":"موفق","data":[10002155613464,30004505000017]}
```

Bulk send has to use one of these numbers. An empty list means the account has no dedicated line; verify send can still work.

### Bulk send

`POST /v1/send/bulk`

One text, one or more mobiles (maximum 100). Omit `sendDateTime` to send immediately. A scheduled time must be from 1 hour ahead to 365 days ahead, as Unix time. A time in the past is rejected (`109`).

```json
{
  "lineNumber": 30004505000017,
  "messageText": "SMS.ir connectivity check",
  "mobiles": ["09120000000"]
}
```

```json
{
  "status": 1,
  "message": "موفق",
  "data": {
    "packId": "2b99e63c-9bf8-4a21-9bfe-3f72dc1b46f1",
    "messageIds": [86522023],
    "cost": 1.0
  }
}
```

The guide's mobile examples accept `0912…`, `912…`, `00912…`, and `+989…`.

Bulk traffic uses your own line. If that line is not a service line, numbers that blocked advertising SMS will not receive it.

### Verify send

`POST /v1/send/verify`

For OTP and other templated messages. The template is created in the panel (ارسال سریع). The call does not take a line number; sms.ir sends it on a service line, including to numbers that blocked advertising SMS, at any hour.

```json
{
  "mobile": "09120000000",
  "templateId": 123456,
  "parameters": [
    {"name": "CODE", "value": "12345"}
  ]
}
```

`name` is the template token without the surrounding `#`. `value` is at most 25 characters. Sandbox has one built-in template: `کد تایید شما: #CODE#`, so the parameter name is `CODE`.

```json
{
  "status": 1,
  "message": "موفق",
  "data": {"messageId": 89545112, "cost": 1.0}
}
```

The public guide's sandbox section shows this URL with method GET. The live route allows POST only (`405` with `Allow: POST` on GET). The app uses POST.

### Delivery report

`GET /v1/send/{messageId}`

```json
{
  "status": 1,
  "message": "موفق",
  "data": {
    "messageId": 89545112,
    "mobile": 9120000000,
    "messageText": "...",
    "sendDateTime": 1628683626,
    "lineNumber": 30004505000017,
    "cost": 1.0,
    "deliveryState": 1,
    "deliveryDateTime": 1628683629
  }
}
```

There is no report row when `messageId` is `0` or `null`.

## Other endpoints (not used by the app)

| Method | Path | Role |
| --- | --- | --- |
| POST | `/v1/send/likeToLike` | Different text per mobile. `messageTexts` and `mobiles` must be the same length. Swagger spells the path `/v1/send/likeTolike`. The live host accepts both, because routing is case-insensitive. |
| DELETE | `/v1/send/scheduled/{packId}` | Cancel a scheduled pack. Allowed until 3 minutes before the send time. |
| GET or POST | `/v1/send?username&password&line&mobile&text` | Legacy URL send. `password` is the API key, not the account password. Prefer the header-authenticated JSON calls. |
| GET | `/v1/send/pack` | Today's packs (`packId`, `recipientCount`, `creationDateTime`). |
| GET | `/v1/send/pack/{packId}` | Messages inside one pack. |
| GET | `/v1/send/live` | Today's sent messages. Page size max 100. |
| GET | `/v1/send/archive` | Older sent messages. `fromDate` and `toDate` are Unix time. |
| GET | `/v1/receive/latest?count=` | Newest inbound SMS. Each message is returned once. |
| GET | `/v1/receive/live` | Inbound SMS from today. |
| GET | `/v1/receive/archive` | Older inbound SMS. |
| GET/POST | `/v1/templates` | List or create verify templates. |
| GET/PUT/DELETE | `/v1/templates/{id}` | Read, update, or delete one template. |

Template verify status values in the spec are `1`, `2`, and `3`. Template type values are `1`, `2`, `3`, and `100`.

## Firewall and IP lock

Published API addresses, if a server firewall filters outbound HTTPS by IP:

- `185.211.56.44` (primary)
- `78.158.166.99` (failover)

Applications should still call `https://api.sms.ir`, not the raw IP. The certificate is issued to `*.sms.ir`.

If the API key itself has an IP allow-list, a server whose public address is not on that list gets `status` 12 even though TLS succeeds.

The live responses include `X-Rate-Limit-Limit: 1h`, `X-Rate-Limit-Remaining`, and `X-Rate-Limit-Reset`. On 30 Sep 2026 the window observed was 20,000 requests per hour.

## What “connected” means

1. DNS resolves `api.sms.ir`.
2. TCP 443 connects.
3. TLS presents a certificate for `sms.ir`.
4. `GET /v1/credit` returns `status` 1.
5. A real send returns `status` 1 and a message id greater than zero.

Steps 1–4 can pass while step 5 fails because of credit, line, template, or blacklist. That still means the network path is open. A message id of zero means the API accepted the call and the number is blacklisted.
