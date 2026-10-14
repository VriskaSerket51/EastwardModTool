#include "main.h"

struct Error {
	const std::string message;
	const std::string filePath;
	const std::string function;
	const std::string source;
	const std::string line;
};

std::string decompile(const char* inputPath) {
	Bytecode bytecode(inputPath);
	Ast ast(bytecode, false, false);
	Lua lua(bytecode, ast, false, false, true);

	bytecode();
	ast();
	std::string output = lua();

	return output;
}

std::string decompile(const uint8_t* data, size_t size) {
  Bytecode bytecode(data, size);
  Ast ast(bytecode, false, false);
  Lua lua(bytecode, ast, false, false, true);
  bytecode();
  ast();
  return lua();
}

void assert(const bool& assertion, const std::string& message, const std::string& filePath, const std::string& function, const std::string& source, const uint32_t& line) {
	if (!assertion) throw Error{
		.message = message,
		.filePath = filePath,
		.function = function,
		.source = source,
		.line = std::to_string(line)
	};
}

std::string byte_to_string(const uint8_t& byte) {
	char string[] = "0x00";
	uint8_t digit;
	
	for (uint8_t i = 2; i--;) {
		digit = (byte >> i * 4) & 0xF;
		string[3 - i] = digit >= 0xA ? 'A' + digit - 0xA : '0' + digit;
	}

	return string;
}
