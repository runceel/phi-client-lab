# Original wphi graphics

These are unchanged graphics from `s7k/wphi`, revision
`d8f11bf60c95d470fa14ca83bcc4c02d5c83fecc`, `resources/phi`.
This collection supports use of PHI with PHI Client Lab. It is not a synthetic
or redrawn substitute. `source-manifest.json` records each imported file's hash.

- `chara/*.bmp`, except `sushi_*.bmp`, are **PCGL-applicable graphics parts**.
  The unchanged `PCGL.txt` and `PCGL.utf8.txt` contain the full license:
  Phi Charactor Graphics License, 2000/10/02 draft 001,
  Copyright (C) 2000 Athena Developer-ML.
  PCGL permits PHI-promoting/supporting use and distribution with the required
  scope declaration and license attachment; it does not permit arbitrary
  non-PHI reuse. The license also covers screenshots containing these graphics.
- `chara/sushi_*.bmp` are expressly excluded from PCGL by the upstream README.
  They were drawn by `phi-client-web/tools/sushi_gra/gen.py` and identified by
  wphi as MIT. `Sushi-MIT.txt` preserves the MIT grant and s7k attribution.
- `Skins/Mine` is the Philly default skin. Its author notice identifies **Cress**.
  wphi's unchanged `README.md` records that its maintainer confirmed reuse on
  2026-11-07. This is an upstream permission record, not a blanket PCGL or MIT
  license for the skin. Preserve the original author and explanation files.
  PHI Client Lab imports these files for local use; it does not bundle the skin
  in its executable or assert additional third-party redistribution rights.

The original `README.md` is retained verbatim, including its provenance and
license boundaries. Only character sheets, the default map sheet, item sheet,
character index and notices are imported. No real-world server configuration,
credentials, gameplay code, other UI skins or sound files are imported.

Sources:

- https://github.com/s7k/wphi/blob/d8f11bf60c95d470fa14ca83bcc4c02d5c83fecc/resources/phi/README.md
- https://github.com/s7k/wphi/blob/d8f11bf60c95d470fa14ca83bcc4c02d5c83fecc/resources/phi/PCGL.utf8.txt
- https://github.com/s7k/phi-client-web/blob/main/LICENSE
