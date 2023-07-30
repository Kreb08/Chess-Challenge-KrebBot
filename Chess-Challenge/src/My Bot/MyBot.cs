//#define LOG
using ChessChallenge.API;
using System;
using System.Linq;

//Tier 1: -8 Elo  +/-18 (1000 Games)
public class MyBot : IChessBot
{
	// Piece values: null, pawn, knight, bishop, rook, queen, king
	private int[] pieceValues = { 0, 100, 300, 330, 500, 900, 0 };
	private int[] pieceValuesL = { 0, 400, 200, 300, 700, 900, 10000 };

	private const ulong k_TpMask = 0x7FFFFF; //4.7 million entries, likely consuming about 151 MB
	private Transposition[] transposTable; // ref  m_TPTable[zHash & k_TpMask]

	const int max_depth = 9; // n + 1, max depth will be 1 smaller
	int max_time;

#if LOG
	int evals = 0;
	int nodes = 0;
#endif

	public MyBot()
	{
		transposTable = new Transposition[k_TpMask + 1];
	}

	public Move Think(Board board, Timer timer)
	{
		if (BitboardHelper.GetNumberOfSetBits(board.AllPiecesBitboard) < 11)
			pieceValues = pieceValuesL; // change values for LateGame

		max_time = timer.MillisecondsRemaining / 30;
		Transposition bestMove = transposTable[board.ZobristKey & k_TpMask];
		for (sbyte i = 1; i < max_depth;)
		{
#if LOG
			evals = 0;
			nodes = 0;
#endif
			CalcRecursive(board, timer, i, board.IsWhiteToMove ? 1 : -1);
			bestMove = transposTable[board.ZobristKey & k_TpMask];
#if LOG
			Console.WriteLine("Depth: {0,2} | Nodes: {1,10} | Evals: {2,10} | Time: {3,5} ms | Best {4} | Eval: {5}", i, nodes, evals, timer.MillisecondsElapsedThisTurn, bestMove.move, bestMove.evaluation);
#endif
			if (bestMove.evaluation + board.PlyCount - 1 == int.MaxValue - i)
				break; // early cheackmate escape

			// time management
			if ((max_time - timer.MillisecondsElapsedThisTurn) < timer.MillisecondsElapsedThisTurn * Math.Pow(i++, 3))
				break;
		}
		return bestMove.move;
	}

	int CalcRecursive(Board board, Timer timer, sbyte currentDepth, int side)
	{
#if LOG
		nodes++;
#endif
		// Cached values
		ref Transposition transposition = ref transposTable[board.ZobristKey & k_TpMask];
		if (transposition.zobristHash == board.ZobristKey && transposition.depth >= currentDepth)
			return transposition.evaluation;

		// End cases
		if (board.IsInCheckmate())
			return int.MinValue + board.PlyCount;
		if (board.IsDraw())
			return (int) (-EvalMaterial(board, side) * 0.2); // return negative score when material advantage
		if (currentDepth <= 0)
			return EvalMaterial(board, side);

		// recursion
		Move[] moves = board.GetLegalMoves();
		ReorderMoves(ref moves);
		Move bestMove = moves[0];
		int bestScore = int.MinValue;
		foreach (Move move in moves)
		{
			board.MakeMove(move);
			int score = -CalcRecursive(board, timer, (sbyte) (currentDepth - 1), -side);
			board.UndoMove(move);
			if (score > bestScore)
			{
				bestScore = score;
				bestMove = move;
			}

			// time management
			if (timer.MillisecondsElapsedThisTurn > max_time)
				break;
		}

		// update cache with best move
		transposition.zobristHash = board.ZobristKey;
		transposition.move = bestMove;
		transposition.evaluation = bestScore;
		transposition.depth = currentDepth;
		return bestScore;
	}

	void ReorderMoves(ref Move[] moves)
	{
		moves.OrderByDescending(m => pieceValues[(int)m.MovePieceType]);
    }

	// pieces of current player - pieces oppenent player + move options current player
	int EvalMaterial(Board board, int side)
	{
#if LOG
		evals++;
#endif
		//bool isWhite = side == 1;
		int eval = 0;
		// all pieces except king
		/*
		for (byte pieceType = 0; ++pieceType < 6;)
		{
			PieceList myPieces = board.GetPieceList((PieceType)pieceType, isWhite);
			eval += myPieces.Count * pieceValues[pieceType];
			eval -= board.GetPieceList((PieceType)pieceType, !isWhite).Count * pieceValues[pieceType];

			// calc mobility for all pieces except pawns
			if (pieceType > 1)
				// gets all possible moves for each piece and counts them
				foreach (Piece piece in myPieces)
					eval += BitboardHelper.GetNumberOfSetBits(~(isWhite ? board.WhitePiecesBitboard : board.BlackPiecesBitboard) & BitboardHelper.GetPieceAttacks(piece.PieceType, piece.Square, board, isWhite));
		}
		*/
		foreach (PieceList pieceList in board.GetAllPieceLists())
		{
			if (pieceList.TypeOfPieceInList == PieceType.King)
				continue;
			int value = pieceList.Count * pieceValues[(int)pieceList.TypeOfPieceInList];
            if (pieceList.IsWhitePieceList == (side == 1))
            {
                eval += value;
                if (pieceList.TypeOfPieceInList != PieceType.Pawn)
                    foreach (Piece piece in pieceList)
						eval += BitboardHelper.GetNumberOfSetBits(~(piece.IsWhite ? board.WhitePiecesBitboard : board.BlackPiecesBitboard) & BitboardHelper.GetPieceAttacks(piece.PieceType, piece.Square, board, piece.IsWhite));
            }
			else 
				eval -= value;
        }
		return eval;
	}

	/*
	int GetDistance(Square square1, Square square2)
	{
		return Math.Abs(square1.File - square2.File) + Math.Abs(square1.Rank - square2.Rank);
	}
	*/

	//14 bytes per entry, likely will align to 16 bytes due to padding (if it aligns to 32, recalculate max TP table size)
	public struct Transposition
	{
		public ulong zobristHash;
		public Move move;
		public int evaluation;
		public sbyte depth;
	};
}