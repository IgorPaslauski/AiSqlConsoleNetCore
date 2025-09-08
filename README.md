# AiSqlConsole

Aplicação **.NET Console** que utiliza **Ollama** + **PostgreSQL** para gerar consultas SQL a partir de perguntas em linguagem natural.  
Inclui registro de métricas de acurácia em um CSV (`ai_sql_metrics.csv`).

---

## 🔧 Requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [PostgreSQL 16+](https://www.postgresql.org/download/)
- [Ollama](https://ollama.ai) rodando localmente com modelo instalado (ex.: `mistral`)

---

## 📦 Instalação

Clone o repositório e restaure os pacotes:

```bash
git clone https://github.com/seu-usuario/AiSqlConsole.git
cd AiSqlConsole
dotnet restore
````

---

## ▶️ Executando

Configure a connection string em `AppConfig.cs`:

```csharp
public const string CONN_STRING =
    "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=123";
```

Garanta que o Ollama esteja rodando, ex.:

```bash
ollama run mistral
```

Depois rode o app:

```bash
dotnet run --project AiSqlConsole
```

---

## 📝 Uso

1. Ao iniciar, o app pedirá uma pergunta:

   ```
   Pergunta (ex.: listar usuarios ativos com a empresa):
   ```
2. Digite em português, por exemplo:

   ```
   quero todos os cards com o nome de cada coluna
   ```
3. O app vai:

   * Consultar o catálogo do banco (`information_schema`)
   * Gerar a SQL via Ollama
   * Executar a consulta
   * Se falhar, tentar corrigir a SQL automaticamente
   * Mostrar o resultado na tela
   * Registrar métricas no arquivo CSV

---

## 📊 Métricas

As métricas de cada execução ficam em `ai_sql_metrics.csv` na pasta do app.

Campos registrados:

* **timestamp\_utc**: data/hora UTC
* **session\_id**: identificador da sessão
* **question**: pergunta original
* **model**: modelo usado (ex.: `mistral`)
* **attempt**: tentativa (1 ou 2)
* **success**: 1 = sucesso, 0 = falha
* **rows**: número de linhas retornadas
* **error\_code** / **error\_message**: erro do PostgreSQL, se houver
* **dur\_ms\_total**, **dur\_ms\_llm**, **dur\_ms\_sql**: tempos em ms
* **sql**: SQL gerada
* **tables\_used**, **columns\_used**: tabelas/colunas referenciadas
* **catalog\_tables**, **catalog\_columns**: tamanho do catálogo fornecido ao modelo
* **system\_prompt\_chars**, **user\_prompt\_chars**: tamanho dos prompts

Exemplo de linha no CSV:

```csv
"2025-09-08 12:15:03","0a1b2c...","quero todos os cards com o nome de cada coluna","mistral",1,0,0,"42703","coluna kc.descricao não existe",512,210,190,"SELECT ...","public.kanban_card;public.kanban_coluna","kc.id;kc.nome;k.nome;k.ordem",2,14,1832,120
```

---

## ⚙️ Configuração extra

* Você pode definir uma variável de ambiente para customizar o caminho do CSV:

  ```bash
  export AI_SQL_CSV_PATH=/var/log/ai_sql_metrics.csv
  ```

---

## 📌 Roadmap

* [ ] Melhorar correção de erros (`42703`, `42P01`, etc.)
* [ ] Suporte a plano JSON (`PLAN → BUILD`) em vez de SQL direto
* [ ] Resumo automático de métricas (dashboard)

---

## 🗑️ `.gitignore`

O projeto já vem com um `.gitignore` configurado para:

* `bin/`, `obj/`, `out/`
* arquivos de IDE (`.vs/`, `.idea/`)
* pacotes NuGet (`packages/`)
* logs e CSV (`ai_sql_metrics.csv`)
* arquivos temporários

---

## 📄 Licença

MIT
