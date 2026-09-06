using System.Threading.Tasks;

// The only thing Mind knows about its backend: send messages (and an
// optional tool schema), get back a normalized ChatResult. Whether
// that's a local Ollama instance or a cloud API is invisible above
// this line.
public interface ILlmProvider
{
    Task<ChatResult> Chat(object[] messages, object[] tools, float temperature = 0.7f);
}
