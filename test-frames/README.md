# test-frames

Screenshots the pipeline is replayed against during development.

This folder starts empty. Running `dotnet run --project tools/Replay` generates a synthetic
corpus automatically so you have something to work with immediately.

Those generated frames are scaffolding. They exercise every stage of the pipeline, but they are
drawn in a system font on a flat gradient, so they tell you nothing about how OCR copes with a
real game's typeface, its translucent dialogue box, or a moving 3D scene behind the text.

Replacing them with real captures is the single most valuable contribution to accuracy. Aim for
~40 PNGs of the text region covering: bright and dark scenes behind the box, every UI scale you
play at, both dialogue and subtitle placements, names with unusual punctuation, long two-line
text, short interjections, and at least one frame captured mid-reveal while the text is still
typing itself out.

Write an `expected.json` alongside them with the correct text for each, and the folder becomes an
OCR accuracy benchmark rather than just a fixture.
