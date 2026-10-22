function ascii(value: string) {
  const cleaned = [...value]
    .map((character) => {
      const code = character.charCodeAt(0);
      return code >= 32 &&
        code <= 126 &&
        character !== "(" &&
        character !== ")" &&
        character !== "\\"
        ? character
        : " ";
    })
    .join("");
  return cleaned.length <= 100 ? cleaned : cleaned.slice(0, 100);
}

function content(lines: string[]) {
  const commands = ["BT", "/F1 9 Tf", "40 760 Td", "14 TL"];
  lines.forEach((line, index) => {
    if (index > 0) commands.push("T*");
    commands.push(`(${ascii(line)}) Tj`);
  });
  commands.push("ET");
  const stream = `${commands.join("\n")}\n`;
  return `<< /Length ${stream.length} >>\nstream\n${stream}endstream`;
}

export function plainPdf(lines: string[]): Uint8Array {
  const source = lines.length === 0 ? [""] : lines;
  const chunks: string[][] = [];
  for (let offset = 0; offset < source.length; offset += 45)
    chunks.push(source.slice(offset, offset + 45));
  const fontId = 3 + chunks.length * 2;
  const objects = ["<< /Type /Catalog /Pages 2 0 R >>", ""];
  const kids: string[] = [];
  chunks.forEach((chunk, index) => {
    const pageId = 3 + index * 2;
    kids.push(`${pageId} 0 R`);
    objects.push(
      `<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents ${pageId + 1} 0 R /Resources << /Font << /F1 ${fontId} 0 R >> >> >>`,
    );
    objects.push(content(chunk));
  });
  objects[1] = `<< /Type /Pages /Count ${chunks.length} /Kids [${kids.join(" ")}] >>`;
  objects.push("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
  let body = "%PDF-1.4\n";
  const offsets = objects.map((object, index) => {
    const offset = body.length;
    body += `${index + 1} 0 obj\n${object}\nendobj\n`;
    return offset;
  });
  const xref = body.length;
  body += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  offsets.forEach((offset) => {
    body += `${String(offset).padStart(10, "0")} 00000 n \n`;
  });
  body += `trailer << /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF`;
  return new TextEncoder().encode(body);
}
