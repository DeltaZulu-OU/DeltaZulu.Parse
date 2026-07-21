# DeltaZulu.Parse

`DeltaZulu.Parse` is a .NET 10 library that parses unstructured log
messages into structured JSON using a liblognorm v2-compatible, PDAG-based
rulebase engine.

## Install from GitHub Packages

This pilot package is available only from the DeltaZulu-OU GitHub Packages
NuGet registry after the publish workflow has completed successfully for the
requested version. A CI artifact alone is not available from the NuGet feed.
Configure the source with a GitHub classic personal access token that has the
least package scopes needed for your access model; do not commit that token or
a credential-bearing `NuGet.Config` file.

```shell
dotnet nuget add source \
  --username <github-user> \
  --password <classic-pat> \
  --store-password-in-clear-text \
  --name deltazulu-github \
  https://nuget.pkg.github.com/DeltaZulu-OU/index.json

dotnet add package DeltaZulu.Parse --version 0.1.0-preview.1
```

`deltazulu-github` is the configured source *name*, not a filesystem path. Do
not pass that name to `dotnet add package --source`; NuGet uses the registered
source and its saved credentials automatically. To target a source explicitly,
pass its full URL instead.

## Quick start

```csharp
using DeltaZulu.Parse;
using System.Text.Json.Nodes;

var context = new ParseContext();
context.LoadSamplesFromString("""
    rule=:%host:word% connected from %ip:ipv4%
    """);

int result = context.Parse("server connected from 192.168.1.1", out JsonObject json);
// result == 0; json contains {"host":"server","ip":"192.168.1.1"}.
```

See the [repository](https://github.com/DeltaZulu-OU/DeltaZulu.Parse) for
rulebase documentation, CLI usage, and development guidance.

## Versioning

`Directory.Build.props` is the single version authority. CI validates that
value and packs a candidate with the identical version. The publish workflow
can also be run manually after the corresponding protected `v<version>` Git tag
has been created: it reads the same source version, validates that tag exists,
publishes the package to GitHub Packages, creates portable self-contained
single-file `lognormalizer` binaries for Linux, macOS, and Windows, and attaches
the binaries next to the `.nupkg` on the GitHub Release. Update the source
version before dispatching the publish workflow; GitHub Packages versions are
immutable.

## License

This package is licensed under the [GNU Affero General Public License v3.0 or
later](https://www.gnu.org/licenses/agpl-3.0.html). The full license text is
included in the package as `LICENSE.txt`.
