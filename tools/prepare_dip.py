"""Apply the small, explicit HumanML-only adapter to the pinned upstream model.

DiP outputs HumanML features; SMPL surface rendering and CLIP are unused. Lazy
imports avoid pulling separate body assets or CLIP's image stack into inference.
The transformer, DistilBERT, diffusion process and trained weights are unchanged.
"""
from pathlib import Path

path = Path(__file__).resolve().parents[1] / "vendor/dip/model/mdm.py"
# Explicit packages prevent unrelated top-level `utils.py` modules in an
# existing scientific Python installation from shadowing DiP's namespace.
for package in ("utils", "model", "diffusion", "data_loaders"):
    marker=path.parents[1] / package / "__init__.py"
    if not marker.exists():marker.write_text("# DiP inference package boundary.\n")
text = path.read_text()
if "self.rot2xyz = None" not in text:
    text = text.replace("import clip\n", "")
    text = text.replace("from model.rotation2xyz import Rotation2xyz\n", "")
    text = text.replace("        self.rot2xyz = Rotation2xyz(device='cpu', dataset=self.dataset)",
                        "        self.rot2xyz = None\n        if self.data_rep != 'hml_vec':\n            from model.rotation2xyz import Rotation2xyz\n            self.rot2xyz = Rotation2xyz(device='cpu', dataset=self.dataset)")
    text = text.replace("        clip_model, clip_preprocess = clip.load(", "        import clip\n        clip_model, clip_preprocess = clip.load(")
    text = text.replace("        self.rot2xyz.smpl_model._apply(fn)", "        if self.rot2xyz is not None: self.rot2xyz.smpl_model._apply(fn)\n        return self")
    text = text.replace("        self.rot2xyz.smpl_model.train(*args, **kwargs)", "        if self.rot2xyz is not None: self.rot2xyz.smpl_model.train(*args, **kwargs)\n        return self")
path.write_text(text)

bert=path.parent/"BERT/BERT_encoder.py"
source=bert.read_text().replace('from_pretrained(modelpath)', 'from_pretrained(modelpath, revision="12040accade4e8a0f71eabdb258fecc2e7e948be")')
bert.write_text(source)
