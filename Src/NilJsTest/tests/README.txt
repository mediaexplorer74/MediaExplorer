Place .js test files in this folder. Each .js file will be executed by NilJsTest --suite Src/NilJsTest/tests.

Naming conventions:
- anyname.js           : plain test file. If it throws an exception, it's counted as a fail for the file's hash.
- anyname.expect.json  : optional JSON describing expected outcome for the test file. Example:
  {
    "shouldFail": true,
    "expectedHash": "12345678",
    "minOccurrences": 1
  }

Use --persist to save observed bad hashes after a suite run. Use --store to point NilJsTest to an existing js_bad_hashes.json.

Examples:
  dotnet run --project Src/NilJsTest -- --suite Src/NilJsTest/tests --persist
  dotnet run --project Src/NilJsTest -- --suite Src/NilJsTest/tests --find-store ..\..\MediaExplorer --copy-store --persist

The runner will not write into bin/obj by default; it prefers the workspace path Src/NilJsTest/js_bad_hashes.json when available.
