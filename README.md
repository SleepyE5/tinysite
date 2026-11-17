# tinysite

A static site generator that fits in one afternoon of reading. Markdown plus
one HTML template in; a site with pages, an archive, tag pages and an RSS feed
out. No packages, no build pipeline, no JavaScript.

It is a .NET global tool. The Markdown converter is hand-written, which is the
main reason the project exists.

```
$ tinysite new my-site && cd my-site
  README.md
  content/about.md
  content/posts/2026-01-01-hello.md
  layout.html
  static/style.css
tinysite: wrote 5 files to my-site.

$ tinysite build --source . --output _site --base-url https://sleepye5.github.io/my-site
tinysite: 2 documents (1 dated), 9 files -> _site in 42 ms
  .tinysite
  about/index.html
  archive/index.html
  feed.xml
  index.html
  posts/hello/index.html
  style.css
  tags/first/index.html
  tags/index.html
```

## Install

It is not on nuget.org. The release carries the package, so you do not need a
checkout or the .NET SDK build to get the tool:

```sh
mkdir -p /tmp/tinysite-pkg
curl -L -o /tmp/tinysite-pkg/tinysite.0.1.0.nupkg \
  https://github.com/SleepyE5/tinysite/releases/download/v0.1.0/tinysite.0.1.0.nupkg
dotnet tool install --tool-path ~/.local/bin \
  --add-source /tmp/tinysite-pkg --version 0.1.0 tinysite
tinysite --version     # tinysite 0.1.0
```

Two things in there are load-bearing, and both bit me while writing them. Keep
the downloaded filename as `tinysite.0.1.0.nupkg`: a local folder feed matches
packages by that name, so `curl -O` into a file called `dl.nupkg` installs
nothing and says the package cannot be found. And keep `--version` — while the
id is unpublished the resolver also asks nuget.org, where `tinysite` is already
somebody else's non-tool package.

From a checkout instead, if you want to change something first:

```sh
git clone https://github.com/SleepyE5/tinysite
cd tinysite && dotnet pack src/tinysite/tinysite.csproj -c Release
dotnet tool install --tool-path ~/.local/bin \
  --add-source ./src/tinysite/bin/Release --version 0.1.0 tinysite
```

The `.csproj` is named rather than the folder, because on an exFAT volume
macOS drops a `._tinysite.csproj` twin next to it and MSBuild then refuses to
guess which one you meant.

## The whole model

One layout, one directory of Markdown, one directory of files to copy.

```
my-site/
  layout.html          the only template
  content/             Markdown, .md
    about.md           -> /about/
    posts/
      2026-01-01-hello.md  -> /posts/hello/   (a post: it has a date)
  static/              copied byte for byte
    style.css          -> /style.css
```

A file in `content/` whose name starts with `2026-01-01-` is a **post**: it
joins `/archive/`, `/tags/` and the feed. Without the date prefix it is a
**page** and appears nowhere but itself. `date:` in front matter overrides the
filename, so correcting a date does not move the URL.

`layout.html` starts with an optional front matter block for the site's own
title, description and language, then the template:

```html
---
title: My site
description: Written by me, published by tinysite.
language: en
---
<!doctype html>
<html lang="{{language}}">
<head><title>{{full_title}}</title><link rel="stylesheet" href="{{base_path}}/style.css"></head>
<body>{{content}}</body>
</html>
```

Placeholders: `title`, `description`, `content`, `date`, `slug`, `url`,
`canonical`, `site_title`, `full_title`, `language`, `base_url`, `base_path`,
`feed_url`, `word_count`, `source_path`. A layout that asks for one nothing
supplies gets one warning naming the placeholder and a page to start from, not
one warning per page.

## What it publishes

For the tree above: one page per document, `/index.html` as the newest posts,
`/archive/index.html` grouped by year, `/tags/index.html` and one page per
tag, and `/feed.xml`.

The home page and the archive exist because the default layout links to them.
A generator whose own template points at a page it never writes ships a 404,
and that is the kind of bug that survives forever because nobody looks.

## Decisions worth knowing about

**Escaping is a property of the value, not of the name.** A placeholder is
HTML-escaped unless the builder registered it with `AddRaw`, so an author's
`title: "<script>alert(1)</script>"` arrives escaped in the `<head>`, while
the converted body keeps its `<strong>`. The old rule -- "`content` is special
and always raw" -- meant every other slot depended on remembering which one
was which.

**A feed URL is never a guess.** `--base-url` has to be absolute http(s)
because every `guid` in an RSS item is built from it, and readers disagree in
delightful ways about relative ones. If you leave it out, the build uses a
localhost address and says so on stderr, rather than publishing a feed that
points at nobody's machine but yours.

**Internal links carry the base path.** A site at
`user.github.io/my-site/` cannot ask the domain root for its own stylesheet,
so generated links are prefixed with the URL's path: `href="/my-site/about/"`.
Absolute feed URLs stay absolute.

**Builds are byte-reproducible.** Output paths and file enumeration are sorted
before use, newlines are LF, the encoding is UTF-8 without BOM, and the feed
carries no `lastBuildDate` because a value derived from the clock makes every
build differ and every diff a lie about content. There is a test that builds
the same source twice and compares every byte, and one that sleeps between
builds to prove the point.

**A broken source publishes nothing.** All documents are parsed before the
first file is written, so a source with one malformed front matter block
produces no half-published site. There is a test for exactly that, because
"it published 40 of 41 pages" is a failure a build log hides.

**`--output` inside `content/` is refused.** Writing the site where the next
build reads its sources is a loop. The conventional `--source . --output
_site` is fine, and the check runs on resolved paths, so `content/../_site` is
allowed and `content/x/..` is not.

**`--clean` needs proof.** It deletes a directory, so it refuses unless the
directory contains the `.tinysite` marker this tool writes. Pointing it at a
hand-written site costs a message, not the site.

**A source tree may claim any URL.** If `content/index.md` exists, the
generated post list is skipped for `/index.html` and the build warns that it
stood down. Same for a tag whose slug a document already occupies.

## What it does not do

No front matter beyond the handful of keys above, no templates beyond
placeholders (`{{content}}` and nothing cleverer), no pagination, no
symlinks, no image resizing, no drafts site, no dev server. The Markdown
converter covers the CommonMark constructs a hand-written site needs and
reports the rest as text; it is not a specification suite, and it is
820 lines you can read.

Links *inside* your Markdown are left exactly as written, which means a
hand-authored `/about/` on a project page will need `{{base_path}}`... except
that placeholders do not run inside Markdown. If that matters to you, use
relative links (`../about/`), which work everywhere and need no prefix at all.

## Tests

```sh
dotnet test tinysite.sln
```

283 tests. The parser and the escaping rules have unit tests; `SiteBuilderTests`
builds real temporary trees end to end, including a byte-for-byte check that
`static/` is copied without reinterpretation and a UTF-8-BOM/CRLF blob; the CLI
tests cover every exit code and the wording of each refusal.

## License

MIT.
