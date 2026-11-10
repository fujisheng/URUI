# Font contract fixtures

These two unmodified fonts and their original asset license were retained from
the upstream sample assets solely for native tests. Lato Latin deliberately lacks
the Hebrew fallback glyph; Roboto Mono supplies distinct metrics. Keeping their
exact bytes preserves the document matching/fallback regression inputs while
removing the upstream sample distribution. Production uses the package fonts.
