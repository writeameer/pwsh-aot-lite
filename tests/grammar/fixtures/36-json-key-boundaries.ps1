# J0 has no command adapter. This fixture protects the upstream parser's
# command-element/string boundaries for the future stock/native JSON-key
# oracle; it does not make either JSON cmdlet executable.
ConvertFrom-Json '{"":1}'
ConvertFrom-Json '{"   ":1}'
ConvertFrom-Json '{"Name":1,"name":2}'
