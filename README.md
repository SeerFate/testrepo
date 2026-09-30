# SMS.ir connectivity check

Small Windows app that answers one question: can this PC call `https://api.sms.ir` the same way a production app would, and will sms.ir accept an SMS from here?

The API notes are in [docs/sms-ir-api.md](docs/sms-ir-api.md).

## Run the window

Double-click:

```text
publish\SmsIrCheck.exe
```

Or, with the .NET 8 SDK:

```text
dotnet run --project src\SmsIrCheck
```

1. Paste the API key from the sms.ir developer panel and click **Save**. The key is encrypted for the current Windows user in `%LocalAppData%\SmsIrCheck\config.json`. It is not written into the program.
2. Click **Check connectivity**. This resolves DNS, opens TCP 443, checks the TLS certificate, then calls `GET /v1/credit` and `GET /v1/line`. It does not send an SMS.
3. Add a recipient, select that row, and click **Send SMS**.
   - **Bulk** posts `/v1/send/bulk` from the sender line, with the text in the message box.
   - **Verify** posts `/v1/send/verify` using a template id and parameters (sandbox's built-in template uses parameter `CODE`).
4. The log shows the HTTP status, the JSON `status` field, and a pass/fail line. After a real send it reads credit again and requests `GET /v1/send/{messageId}`.

A sandbox key uses the same URLs and does not deliver a real SMS or spend credit. A production key does both.

`message id 0` means sms.ir accepted the call and the number is blacklisted. There is no delivery report for that id.

## Check a server from a terminal

```text
SmsIrCheck.exe --probe
```

Exit codes: `0` key accepted, `2` network failed, `3` key missing or rejected, `4` the host answered but rejected the account call.

```text
SmsIrCheck.exe --send --yes --mobile 0912xxxxxxx --line 3000xxxx --text "hello"
SmsIrCheck.exe --send --yes --mobile 0912xxxxxxx --template 123456 --param CODE=12345
```

The key is taken from the `SMSIR_API_KEY` environment variable, otherwise from the key saved in the window. `--send` without `--yes` prints what it would do and does not send.

## Rebuild the single file

```text
dotnet publish src\SmsIrCheck\SmsIrCheck.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

The published exe includes the .NET 8 runtime, so the server does not need a separate install. Opening the window hides the extra console. `--probe` and `--send` keep the console and print the report.
