"""Report access only; never print credentials, response headers or signed URLs."""
from huggingface_hub import HfApi, get_token
from huggingface_hub.utils import GatedRepoError, HfHubHTTPError
token=get_token()
if not token:
    print("HF_LOGIN_REQUIRED")
else:
    try:
        HfApi(token=token).auth_check(repo_id="meta-llama/Meta-Llama-3-8B-Instruct",repo_type="model")
        print("LLAMA_ACCESS_OK")
    except GatedRepoError:
        print("LLAMA_APPROVAL_REQUIRED")
    except Exception as error:
        print("ACCESS_CHECK_FAILED",type(error).__name__)
