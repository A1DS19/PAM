# 21G Casino WSDLs

Snapshot of GBS's live `.asmx` WSDLs for the 21G integration, captured
2026-05-12 from `http://api.betanything.eu/integrations/21GCasino/`.
The `http://api.betanysports.eu/...` host serves byte-identical WSDLs
except for the `<soap:address location>` value — both brands run the
same GBS deployment.

These are committed so we can:
1. Drive the SoapCore service contract in `Pam.Ingest/Vendors/TwentyOneG/`
   off the exact production schema.
2. Diff future GBS deployments against this baseline — any drift in the
   operation set, message shape, or namespace breaks our intercept.
3. Reference the contract during the strangler migration without
   needing a running GBS reachable from a dev box.

## Files

| File | Maps to |
|---|---|
| `CustomerTransaction21G.wsdl` | Risk / Win posting — the only endpoint that mutates state |
| `ValidateSessionID21GCasino.wsdl` | Session validity check (read-side) |
| `GetCustomerBalance21GCasino.wsdl` | Balance read (read-side) |

The fourth `.asmx` (combined `21gCasino.asmx`) returns 500 on `?WSDL` in
production — known bug, a name collision between method
`GetCustomerBalance` and a type with `[XmlRoot("GetCustomerBalance")]`
in GBS's C# source. 21G doesn't use it. Not included.

## Key properties of the contract

- **One operation per endpoint, all named `PostTransaction`**, all three
  with the SAME 12-string-field request shape. Behavior differs by URL.
- **Target namespace**: `http://tempuri.org/` (default, never customized).
- **SOAP 1.1 and 1.2** both advertised; clients can use either.
- **Auth in the body** as `systemID` + `systemPassword` parameters; no
  HTTP-level auth (no client cert, no bearer, no HMAC).
- **No idempotency key** in the wire format. We derive one (SHA-256
  over canonical request fields) inside `Pam.Ingest` so the UNIQUE
  constraint catches retries.
- **`amount` is a string** — parse server-side; may include sign or
  decimals.
- **`dailyFigureDate_YYYYMMDD`** is a string in `yyyyMMdd` format —
  use `DateOnly.ParseExact(value, "yyyyMMdd", InvariantCulture)`.

## Refreshing

```bash
mkdir -p /tmp/21g-wsdl && cd /tmp/21g-wsdl
for path in 'integrations/21GCasino/CustomerTransaction21G.asmx' \
            'integrations/21GCasino/ValidateSessionID21GCasino.asmx' \
            'integrations/21GCasino/GetCustomerBalance21GCasino.asmx'; do
  curl -s -o "$(basename "$path" .asmx).wsdl" \
    "http://api.betanything.eu/${path}?WSDL"
done
diff -u /Users/jose.padilla/Desktop/work/pam/infra/wsdl/21g/ /tmp/21g-wsdl/
```

If the diff is empty, the contract is unchanged. If anything besides
`<soap:address>` differs, we have drift to investigate.
