import argparse


def identity_handler(_args: argparse.Namespace) -> None:
    import json

    from scoring.identity import score_identity

    result = score_identity(_args.reference, _args.render)
    print(json.dumps(result))


def subject_handler(_args: argparse.Namespace) -> None:
    import json

    from scoring.subject import score_subject

    result = score_subject(_args.image_a, _args.image_b)
    print(json.dumps(result))


def adherence_handler(_args: argparse.Namespace) -> None:
    import json

    from scoring.adherence import score_adherence

    result = score_adherence(_args.render, _args.prompt)
    print(json.dumps(result))


def presence_handler(_args: argparse.Namespace) -> None:
    import json

    from scoring.presence import score_presence

    result = score_presence(_args.render, _args.expected)
    print(json.dumps(result))


def sanitisation_handler(_args: argparse.Namespace) -> None:
    import json

    from scoring.sanitisation import score_sanitisation

    result = score_sanitisation(_args.render)
    print(json.dumps(result))


def scorecard_handler(_args: argparse.Namespace) -> None:
    import json

    from scoring.scorecard import build_scorecard

    build_scorecard(_args.manifest, _args.renders, _args.out, _args.references)
    print(json.dumps({"json": f"{_args.out}/scorecard.json", "markdown": f"{_args.out}/scorecard.md"}))


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Consistency scoring tool skeleton.")
    subparsers = parser.add_subparsers(dest="command", required=True)

    commands = (
        ("identity", identity_handler, "Score identity consistency."),
        ("subject", subject_handler, "Score subject consistency."),
        ("adherence", adherence_handler, "Score prompt adherence."),
        ("presence", presence_handler, "Score subject presence."),
        ("sanitisation", sanitisation_handler, "Score prompt sanitisation."),
        ("scorecard", scorecard_handler, "Build the consistency scorecard."),
    )
    for name, handler, help_text in commands:
        subparser = subparsers.add_parser(name, help=help_text, description=help_text)
        subparser.set_defaults(handler=handler)

    identity_parser = subparsers.choices["identity"]
    identity_parser.add_argument("--reference", required=True)
    identity_parser.add_argument("--render", required=True)

    subject_parser = subparsers.choices["subject"]
    subject_parser.add_argument("--image-a", required=True)
    subject_parser.add_argument("--image-b", required=True)

    adherence_parser = subparsers.choices["adherence"]
    adherence_parser.add_argument("--render", required=True)
    adherence_parser.add_argument("--prompt", required=True)

    presence_parser = subparsers.choices["presence"]
    presence_parser.add_argument("--render", required=True)
    presence_parser.add_argument("--expected", required=True, type=int)

    sanitisation_parser = subparsers.choices["sanitisation"]
    sanitisation_parser.add_argument("--render", required=True)

    scorecard_parser = subparsers.choices["scorecard"]
    scorecard_parser.add_argument("--manifest", required=True)
    scorecard_parser.add_argument("--renders", required=True)
    scorecard_parser.add_argument("--out", default="artifacts/tmp/consistency-scoring")
    scorecard_parser.add_argument("--references")

    return parser


def main() -> None:
    parser = build_parser()
    args = parser.parse_args()
    args.handler(args)


if __name__ == "__main__":
    main()
