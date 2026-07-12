#!/usr/bin/env bash
set -euo pipefail

ruby <<'RUBY'
files = ['README.md'] + Dir['docs/**/*.md']
missing = []

files.each do |file|
  File.readlines(file).each_with_index do |line, index|
    line.scan(/\[[^\]]+\]\(([^)#]+)(?:#[^)]+)?\)/).flatten.each do |link|
      next if link.match?(/\Ahttps?:/)

      path = File.expand_path(link, File.dirname(file))
      missing << "#{file}:#{index + 1}: missing #{link}" unless File.exist?(path)
    end
  end
end

if missing.any?
  warn missing.join("\n")
  exit 1
end

puts "Documentation links resolve"
RUBY
