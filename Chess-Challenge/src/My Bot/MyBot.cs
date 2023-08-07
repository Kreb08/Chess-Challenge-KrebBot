//#define LOG
using ChessChallenge.API;
using System;
using System.Linq;

//Tier 1: +601 Elo  +/-57 (1000 Games)
// W: 956 D: 27 L: 17 Timeouts: 14 (20s)
public class MyBot : IChessBot
{
	// Piece values: null, pawn, knight, bishop, rook, queen, king
	private int[] pieceValues = { 0, 100, 300, 330, 500, 900, 0 };

	private const ulong k_TpMask = 0x7FFFFF; //4.7 million entries, likely consuming about 151 MB
	private readonly Transposition[] transposTable; // ref  m_TPTable[zHash & k_TpMask]

    private const ulong k_pTpMask = 0xFFFFF; // 20 bits, 1048575 entries
    private readonly PawnTransposition[] pawnTranspositions; // ref  m_TPTable[zHash & k_pTpMask]

	// ulong 1 is WHITE BOTTOM LEFT (A1), MAX VALUE is EVERY TILE (11111....)

	// shift by rank, flip for black
	// this is also an isolation table when masking the 'file' (middle coloumn) ..111.. => ..101..
    private readonly ulong[] passedPawnTableW = {
		0b00000000_00000011_00000011_00000011_00000011_00000011_00000011_00000000,
		0b00000000_00000111_00000111_00000111_00000111_00000111_00000111_00000000,
		0b00000000_00001110_00001110_00001110_00001110_00001110_00001110_00000000,
		0b00000000_00011100_00011100_00011100_00011100_00011100_00011100_00000000,
		0b00000000_00111000_00111000_00111000_00111000_00111000_00111000_00000000,
		0b00000000_01110000_01110000_01110000_01110000_01110000_01110000_00000000,
		0b00000000_11100000_11100000_11100000_11100000_11100000_11100000_00000000,
        0b00000000_11000000_11000000_11000000_11000000_11000000_11000000_00000000
    };

    int max_depth = 11; // n + 1, max depth will be 1 smaller
	int max_time;

#if LOG
	int evals = 0;
	int nodes = 0;
	int pawnEvals = 0;
#endif

	public MyBot()
	{
		transposTable = new Transposition[k_TpMask + 1];
        pawnTranspositions = new PawnTransposition[k_pTpMask + 1];
	}

	public Move Think(Board board, Timer timer)
    {
        max_time = timer.MillisecondsRemaining / (int) (50 - Math.Log(board.PlyCount + 1));
		Transposition bestMove = transposTable[board.ZobristKey & k_TpMask];

		if (timer.MillisecondsRemaining < 1000)
			max_depth = 4;

		for (sbyte i = 1; i < max_depth;i++)
		{
#if LOG
			evals = 0;
			nodes = 0;
            pawnEvals = 0;
#endif
			CalcRecursive(board, i, board.IsWhiteToMove ? 1 : -1, int.MinValue+1, int.MaxValue);
			bestMove = transposTable[board.ZobristKey & k_TpMask];
#if LOG
			Console.WriteLine("Depth: {0,2} | Nodes: {1,8} | Evals: {2,8} | Pawns: {3,4} | Time: {4,5} ms | Best {5} | Eval: {6}", i, nodes, evals, pawnEvals, timer.MillisecondsElapsedThisTurn, bestMove.move, bestMove.evaluation);
#endif
			if (bestMove.evaluation + board.PlyCount - 1 == int.MaxValue - i)
				break; // early cheackmate escape

			// time management
			if (timer.MillisecondsElapsedThisTurn * 4 > max_time)
				break;
		}

#if LOG
		Console.WriteLine("EllapsedTime: {0,5} ms | MaxTime: {1,5} ms | TimeLeft: {2,5} ms", timer.MillisecondsElapsedThisTurn, max_time, timer.MillisecondsRemaining);
#endif
		return bestMove.move;
	}

    int CalcRecursive(Board board, sbyte currentDepth, int side, int alpha, int beta)
	{
#if LOG
		nodes++;
#endif
        // End cases
        if (board.IsInCheckmate())
            return int.MinValue + board.PlyCount;
        if (board.IsDraw())
            return 0;

        // Cached values
        ref Transposition transposition = ref transposTable[board.ZobristKey & k_TpMask];
		if (transposition.zobristHash == board.ZobristKey && transposition.depth >= currentDepth)
			return transposition.evaluation;

		if (currentDepth <= 0)
			return EvalMaterial(board, side);

		Span<Move> moves = stackalloc Move[256];
		board.GetLegalMovesNonAlloc(ref moves);
        int[] scores = new int[moves.Length];

        // Score moves
        for (int i = 0; i < moves.Length; i++)
        {
            Move move = moves[i];
            // TT move
            if (move == transposition.move) 
				scores[i] = 1000000;
            // https://www.chessprogramming.org/MVV-LVA
            else if (move.IsCapture)
				scores[i] = 100 * (int)move.CapturePieceType - (int)move.MovePieceType;
        }


		Move bestMove = moves[0];
		int bestScore = int.MinValue;
        for (int i = 0; i < moves.Length; i++)
        {
            // Incrementally sort moves
            for (int j = i + 1; j < moves.Length; j++)
            {
                if (scores[j] > scores[i])
                    (scores[i], scores[j], moves[i], moves[j]) = (scores[j], scores[i], moves[j], moves[i]);
            }

			Move move = moves[i];
            board.MakeMove(move);
			int score = -CalcRecursive(board, (sbyte) (currentDepth - 1), -side, -beta, -alpha);
			board.UndoMove(move);
			if (score >= beta)
			{
				// Fail soft beta
				bestScore = score;
				bestMove = move;
				break;
			}
			if (score > bestScore)
            {
				bestScore = score;
                bestMove = move;
				alpha = Math.Max(alpha, score);
            }
		}

		// update cache with best move
		transposition.zobristHash = board.ZobristKey;
		transposition.move = bestMove;
		transposition.evaluation = bestScore;
		transposition.depth = currentDepth;
		return bestScore;
	}

	// pieces of current player - pieces oppenent player + move options current player
	int EvalMaterial(Board board, int side)
	{
#if LOG
		evals++;
#endif
        int eval = EvalPawns(board, side);
		foreach (PieceList pieceList in board.GetAllPieceLists())
		{
			int value = pieceList.Count * pieceValues[(int)pieceList.TypeOfPieceInList];
			if (pieceList.IsWhitePieceList == (side == 1))
			{
				eval += value;
				if (pieceList.TypeOfPieceInList != PieceType.Pawn && pieceList.TypeOfPieceInList != PieceType.King)
                {
					foreach (Piece piece in pieceList)
                        eval += BitboardHelper.GetNumberOfSetBits(~(piece.IsWhite ? board.WhitePiecesBitboard : board.BlackPiecesBitboard) & BitboardHelper.GetPieceAttacks(piece.PieceType, piece.Square, board, piece.IsWhite));
                }
			}
			else 
				eval -= value;
		}
		return eval;
	}

	int EvalPawns(Board board, int side)
    {
		ulong myPawns = board.GetPieceBitboard(PieceType.Pawn, side == 1);
		ulong enemyPawns = board.GetPieceBitboard(PieceType.Pawn, side != 1);
        ref PawnTransposition pawnTransposition = ref pawnTranspositions[myPawns & k_pTpMask];
        if (pawnTransposition.pawnKey == myPawns && pawnTransposition.side == side)
            return pawnTransposition.evaluation;

#if LOG
        pawnEvals++;
#endif
        int score = 0;
        ulong pawns = myPawns;
        while (BitboardHelper.GetNumberOfSetBits(pawns) > 0)
        {
			int index = BitboardHelper.ClearAndGetIndexOfLSB(ref pawns),
				file = index & 7;

			if ((side == 1 ? passedPawnTableW[file] << (index & 56) : BitConverter.ToUInt64(BitConverter.GetBytes(passedPawnTableW[file] << ((index ^ 56) & 56)).Take(8).Reverse().ToArray(), 0) & enemyPawns) == 0)
				score += 51;// passed Pawn
            else if (((passedPawnTableW[file] ^ 282578800148736u << file) & myPawns) == 0)
                score -= 10; // isolated Pawn
        }
		
        pawnTransposition.pawnKey = myPawns;
		pawnTransposition.side = (sbyte) side;
		pawnTransposition.evaluation = score;
		return score;
    }

    //14 bytes per entry, likely will align to 16 bytes due to padding (if it aligns to 32, recalculate max TP table size)
	public struct Transposition
	{
		public ulong zobristHash;
		public Move move;
		public int evaluation;
		public sbyte depth;
		public sbyte bound;
	};

	public struct PawnTransposition
	{
		public ulong pawnKey;
		public int evaluation;
		public sbyte side;
	};
}